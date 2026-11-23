using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Default <see cref="IElevatedRunService"/> (FR.15.16 to FR.15.19). The records live in memory and in <c>elevated-runs\&lt;id&gt;\request.json</c>; every status
/// change is a compare-and-set under <see cref="_lock"/>, and everything that follows a change (log line, event, Focus items, outcome turn) runs after the lock.
/// The root folder, the clock, the elevation flag and the process probe are constructor parameters, so tests run it over a temp folder with a fake clock.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ElevatedRunService : IElevatedRunService, IDisposable
{
	private readonly IAppSettingsService _settings;
	private readonly ISessionRegistry _registry;
	private readonly IAttentionService _attention;
	private readonly Lazy<ISchedulerService> _scheduler;
	private readonly IUserMessageChannels _channels;
	private readonly TimeProvider _clock;
	private readonly string _root;
	private readonly bool _isElevated;
	private readonly Func<int, bool> _isProcessAlive;
	private readonly ILogger _log;

	private readonly ElevatedRunLaunchBuilder _launch = new();
	private readonly ElevatedRunWorkingDirRule _dirRule = new();
	private readonly ElevatedRunOutputReader _outputReader = new();
	private readonly ElevatedRunOutcomeBuilder _outcome;
	private readonly ElevatedRunItemBuilder _items = new();
	private readonly JsonSidecarFile _file = new();

	private readonly object _lock = new();
	private readonly Dictionary<string, ElevatedRunModel> _runs = new(StringComparer.Ordinal);
	private Timer? _timer;
	private int _polling;

	public event EventHandler<ElevatedRunChangedEventArgs>? Changed;

	/// <param name="rootDir">The folder that holds the run folders; null = <c>&lt;data root&gt;\elevated-runs</c>.</param>
	/// <param name="isElevated">Whether the daemon runs elevated; null = ask the process.</param>
	/// <param name="isProcessAlive">Whether the wrapper's process still exists; null = look at the process table.</param>
	/// <param name="log">The logger of the audit lines; null = the application log.</param>
	public ElevatedRunService(
		IAppSettingsService settings,
		ISessionRegistry registry,
		IAttentionService attention,
		Lazy<ISchedulerService> scheduler,
		IUserMessageChannels channels,
		string? rootDir = null,
		TimeProvider? clock = null,
		bool? isElevated = null,
		Func<int, bool>? isProcessAlive = null,
		ILogger? log = null)
	{
		_settings       = settings;
		_registry       = registry;
		_attention      = attention;
		_scheduler      = scheduler;
		_channels       = channels;
		_root           = rootDir ?? Path.Combine(Constants.DataRoot, Constants.ElevatedRuns.FolderName);
		_clock          = clock ?? TimeProvider.System;
		_isElevated     = isElevated ?? Environment.IsPrivilegedProcess;
		_isProcessAlive = isProcessAlive ?? ProcessExists;
		_log            = log ?? Log.ForContext<ElevatedRunService>();
		_outcome        = new ElevatedRunOutcomeBuilder(_outputReader);
		_attention.SetRunProgress(IsInProgress);
	}

	/// <summary>FR.23.41: the run is launching or running, so its Focus item is in progress. False for an unknown run.</summary>
	private bool IsInProgress(string runId)
	{
		lock (_lock)
			return _runs.TryGetValue(runId, out var run) && run.Status is ElevatedRunStatus.Launching or ElevatedRunStatus.Running;
	}

	public bool IsDaemonElevated => _isElevated;

	public void Start()
	{
		Reconcile();
		_timer = new Timer(_ => PollSafe(), null, Constants.ElevatedRuns.PollInterval, Constants.ElevatedRuns.PollInterval);
	}

	public void Stop()
	{
		_timer?.Dispose();
		_timer = null;
	}

	public void Dispose() => Stop();

	// ── Request (FR.15.16) ────────────────────────────────────────────────────

	public async Task<ElevatedRunRequestOutcomeModel> RequestAsync(SessionNodeModel caller, ElevatedRunRequestModel request)
	{
		if (_isElevated)
			throw new McpToolException(Constants.ElevatedRuns.DaemonElevatedText);

		var command    = CheckCommand(request.Command);
		var reason     = CheckReason(request.Reason);
		var timeout    = CheckTimeout(request.TimeoutMinutes);
		var workingDir = _dirRule.Resolve(
			string.IsNullOrWhiteSpace(request.WorkingDir) ? throw new McpToolException(Constants.ElevatedRuns.WorkingDirRequiredText) : request.WorkingDir,
			caller.WorkingDirectory, RegisteredDirectories());

		var topLevel = FindTopLevel(caller);
		ElevatedRunModel created;
		lock (_lock)
		{
			var same = _runs.Values.FirstOrDefault(r => r.Status == ElevatedRunStatus.Pending && r.RequesterNodeId == caller.NodeId
				&& string.Equals(r.Command, command, StringComparison.Ordinal) && string.Equals(r.WorkingDir, workingDir, StringComparison.OrdinalIgnoreCase));
			if (same != null)
				return new ElevatedRunRequestOutcomeModel(same.Copy(), false);

			CheckLimitsLocked(caller.NodeId);

			var now = _clock.GetUtcNow();
			created = new ElevatedRunModel
			{
				Id              = NewIdLocked(),
				RequesterNodeId = caller.NodeId,
				RequesterName   = caller.Name,
				TopLevelNodeId  = topLevel?.NodeId,
				TopLevelName    = topLevel?.Name,
				Reason          = reason,
				WorkingDir      = workingDir,
				Command         = command,
				TimeoutMinutes  = timeout,
				CreatedUtc      = now,
				ExpiresUtc      = now + Constants.ElevatedRuns.PendingLifetime,
				Status          = ElevatedRunStatus.Pending,
				StatusUtc       = now,
			};

			// The launch line must fit now, so that the request that is shown can always be started.
			_launch.Build(created, RunDir(created.Id));

			Directory.CreateDirectory(RunDir(created.Id));
			SaveLocked(created);
			_runs[created.Id] = created;
		}

		var snapshot = created.Copy();
		LogChange(snapshot);
		PinItems(snapshot, caller, topLevel);
		RaiseChanged(snapshot);
		await NotifyRequestedAsync(snapshot, caller, topLevel);
		return new ElevatedRunRequestOutcomeModel(snapshot, true);
	}

	private static string CheckCommand(string? command)
	{
		if (string.IsNullOrWhiteSpace(command) || command.Length > Constants.ElevatedRuns.MaxCommandLength || command.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0)
			throw new McpToolException(Constants.ElevatedRuns.CommandInvalidText);

		return command;
	}

	private static string CheckReason(string? reason)
	{
		var oneLine = string.Join(' ', (reason ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
		if (oneLine.Length == 0)
			throw new McpToolException(Constants.ElevatedRuns.ReasonRequiredText);

		return oneLine.Length > Constants.ElevatedRuns.MaxReasonLength ? oneLine[..Constants.ElevatedRuns.MaxReasonLength] : oneLine;
	}

	private static int CheckTimeout(JsonNode? node)
	{
		if (node == null)
			return Constants.ElevatedRuns.DefaultTimeoutMinutes;

		// A JsonValue made in code (an int) and one read from the wire (a JsonElement) answer TryGetValue differently: look at the JSON they stand for.
		var element = JsonSerializer.SerializeToElement(node);
		double value;
		if (element.ValueKind == JsonValueKind.Number)
			value = element.GetDouble();
		else if (element.ValueKind == JsonValueKind.String
		         && double.TryParse(element.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
			value = parsed;
		else
			throw new McpToolException(Constants.ElevatedRuns.TimeoutInvalidText);

		if (value != Math.Floor(value) || value < Constants.ElevatedRuns.MinTimeoutMinutes || value > Constants.ElevatedRuns.MaxTimeoutMinutes)
			throw new McpToolException(Constants.ElevatedRuns.TimeoutInvalidText);

		return (int)value;
	}

	private IReadOnlyList<string> RegisteredDirectories()
	{
		lock (_settings.SyncRoot)
			return _settings.Settings.Tree.Select(d => d.Path).ToList();
	}

	private void CheckLimitsLocked(string nodeId)
	{
		var open = _runs.Values.Where(r => r.Status is ElevatedRunStatus.Pending or ElevatedRunStatus.Launching).ToList();
		if (open.Count(r => r.RequesterNodeId == nodeId) >= Constants.ElevatedRuns.MaxOpenPerSession)
			throw new McpToolException(Constants.ElevatedRuns.SessionLimitText);
		if (open.Count >= Constants.ElevatedRuns.MaxOpenTotal)
			throw new McpToolException(Constants.ElevatedRuns.TotalLimitText);
	}

	/// <summary>8 lowercase hex characters, unique among the records and the run folders.</summary>
	private string NewIdLocked()
	{
		string id;
		do
			id = Guid.NewGuid().ToString("N")[..Constants.ElevatedRuns.IdLength];
		while (_runs.ContainsKey(id) || Directory.Exists(RunDir(id)));

		return id;
	}

	/// <summary>The end of the SupervisorNodeId chain (FR.23.31); null for a top-level requester, and when the chain leaves the tree.</summary>
	private SessionNodeModel? FindTopLevel(SessionNodeModel requester)
	{
		if (requester.OrchestrationDepth <= 0 || string.IsNullOrEmpty(requester.SupervisorNodeId))
			return null;

		var visited = new HashSet<string>(StringComparer.Ordinal) { requester.NodeId };
		var current = requester;
		while (!string.IsNullOrEmpty(current.SupervisorNodeId))
		{
			if (!visited.Add(current.SupervisorNodeId))
				return null;

			var next = _registry.FindModelByNodeId(current.SupervisorNodeId);
			if (next == null)
				return null;

			current = next;
		}

		return current;
	}

	// ── Cancel and list (FR.15.17) ────────────────────────────────────────────

	public string Cancel(SessionNodeModel caller, string requestId)
	{
		var run = FindOwn(caller.NodeId, requestId);
		var cancelled = Transition(run.Id, [ElevatedRunStatus.Pending], ElevatedRunStatus.Cancelled,
			r => r.CancelledBy = ElevatedRunCancelledBy.Agent);
		if (cancelled != null)
			return string.Format(Constants.ElevatedRuns.WithdrawnFormat, run.Id);

		var current = Find(run.Id)!;
		var name    = Constants.ElevatedRuns.StatusNames[(int)current.Status];
		return string.Format(current.IsFinal ? Constants.ElevatedRuns.AlreadyFinalFormat : Constants.ElevatedRuns.AlreadyActiveFormat, current.Id, name);
	}

	/// <summary>The caller's own request by id or unique prefix; another session's id and an unknown id give the same error.</summary>
	private ElevatedRunModel FindOwn(string nodeId, string idOrPrefix)
	{
		var id = idOrPrefix.Trim().ToLowerInvariant();
		if (id.Length == 0)
			throw new McpToolException(Constants.ElevatedRuns.IdRequiredText);

		lock (_lock)
		{
			if (_runs.TryGetValue(id, out var exact) && exact.RequesterNodeId == nodeId)
				return exact.Copy();

			if (id.Length < Constants.ElevatedRuns.IdPrefixMinLength)
				throw new McpToolException(string.Format(Constants.ElevatedRuns.IdTooShortFormat, id));

			var matches = _runs.Values.Where(r => r.RequesterNodeId == nodeId && r.Id.StartsWith(id, StringComparison.Ordinal)).ToList();
			return matches.Count switch
			{
				0 => throw new McpToolException(string.Format(Constants.ElevatedRuns.UnknownRunFormat, id)),
				1 => matches[0].Copy(),
				_ => throw new McpToolException(string.Format(Constants.ElevatedRuns.IdAmbiguousFormat, id)),
			};
		}
	}

	public string ListText(SessionNodeModel caller, string? status)
	{
		var all = (status?.Trim().ToLowerInvariant()) switch
		{
			null or "" or RemoteProtocol.ElevatedRuns.ListActive => false,
			RemoteProtocol.ElevatedRuns.ListAll => true,
			var other => throw new McpToolException(string.Format(Constants.ElevatedRuns.ListInvalidStatusFormat, other)),
		};

		var runs = List(caller.NodeId, all, int.MaxValue);
		return runs.Count == 0 ? Constants.ElevatedRuns.NoRunsText : string.Join('\n', runs.Select(ListLine));
	}

	private string ListLine(ElevatedRunModel run)
	{
		var line = string.Format(Constants.ElevatedRuns.ListLineFormat, run.Id, JsonNamingPolicy.CamelCase.ConvertName(run.Status.ToString()),
			Cut(run.Command, Constants.ElevatedRuns.ListCommandLength), Cut(run.Reason, Constants.ElevatedRuns.ListReasonLength));
		if (!run.IsFinal)
			return line;

		if (run.ExitCode is { } code && _outcome.Duration(run) is { } duration)
			return line + string.Format(Constants.ElevatedRuns.ListExitSuffixFormat, code, _outcome.FormatDuration(duration));

		return string.IsNullOrEmpty(run.Failure) ? line : line + string.Format(Constants.ElevatedRuns.ListFailureSuffixFormat, run.Failure);
	}

	public IReadOnlyList<ElevatedRunModel> List(string? nodeId, bool all, int max)
	{
		lock (_lock)
			return _runs.Values
				.Where(r => (nodeId == null || r.RequesterNodeId == nodeId) && (all || r.IsOpen))
				.OrderByDescending(r => r.CreatedUtc)
				.Take(max)
				.Select(r => r.Copy())
				.ToList();
	}

	public ElevatedRunModel? Find(string requestId)
	{
		lock (_lock)
			return _runs.TryGetValue(requestId, out var run) ? run.Copy() : null;
	}

	// ── Run folder ────────────────────────────────────────────────────────────

	public string GetRunDir(string requestId) => RunDir(requestId);

	public string GetLogPath(string requestId) => Path.Combine(RunDir(requestId), Constants.ElevatedRuns.OutputLogFileName);

	public string GetOutputTail(string requestId, int lines) =>
		string.Join('\n', _outputReader.ReadTail(GetLogPath(requestId), lines, Constants.ElevatedRuns.OutcomeTailLineLength, int.MaxValue));

	private string RunDir(string id) => Path.Combine(_root, id);

	private string FilePath(string id, string name) => Path.Combine(RunDir(id), name);

	/// <summary>Writes <c>request.json</c> atomically (temp file, then rename).</summary>
	private void SaveLocked(ElevatedRunModel run) => _file.Write(FilePath(run.Id, Constants.ElevatedRuns.RequestFileName), run);

	private bool ProcessExists(int pid)
	{
		try
		{
			using var process = Process.GetProcessById(pid);
			return !process.HasExited;
		}
		catch (ArgumentException)
		{
			return false;
		}
		catch (Exception ex)
		{
			_log.Debug(ex, "ElevatedRun: cannot tell whether process {Pid} exists; assuming it does", pid);
			return true;
		}
	}

	private static string Cut(string text, int maxLength) => text.Length > maxLength ? text[..maxLength] : text;
}
