using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services.ElevatedRuns;

/// <summary>
/// Fixture E of TC.ER.01 over a temp root: projects Alpha and Beta registered; folders Alpha\src, Alpha-wt-fix(\src), Alpha2, Alpha-wt, Alphawt-x, Other, a folder
/// <c>Elsewhere\Alpha-wt-x</c> (another parent, in place of <c>D:\Alpha-wt-x</c>) and a junction Alpha\link to a folder outside; sessions Lead (in Alpha), its worker Wrk
/// (in Alpha-wt-fix) and Other (in Beta). The real <see cref="ElevatedRunService"/> runs on a manual clock, over a real <see cref="AttentionService"/> and
/// a recording scheduler and notifier. Disposing deletes the temp root.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ElevatedRunTestEnv : IDisposable
{
	public string Root { get; } = Path.Combine(Path.GetTempPath(), "er_tests_" + Guid.NewGuid().ToString("N")[..12]);
	public string Proj => Path.Combine(Root, "Proj");
	public string Alpha => Path.Combine(Proj, "Alpha");
	public string Beta => Path.Combine(Proj, "Beta");
	public string RunsRoot => Path.Combine(Root, "runs");

	public AppSettingsService Settings { get; }
	public SessionRegistry Registry { get; }
	public RecordingSessionFileService AttentionFiles { get; } = new();
	public AttentionService Attention { get; }
	public RecordingScheduler Scheduler { get; } = new();
	public FakeUserChannels Channels { get; } = new();
	public ManualTimeProvider Clock { get; } = new();
	public CollectingSink Sink { get; } = new();
	public ElevatedRunService Service { get; private set; }
	public AgentMcpServer Server { get; }

	/// <summary>Which wrapper PIDs the fake process probe reports as alive.</summary>
	public HashSet<int> AlivePids { get; } = [];

	public SessionNodeModel Lead { get; }
	public SessionNodeModel Wrk { get; }
	public SessionNodeModel Other { get; }

	private readonly bool _isElevated;

	public ElevatedRunTestEnv(bool isElevated = false)
	{
		_isElevated = isElevated;
		foreach (var dir in new[] { "Alpha", @"Alpha\src", "Alpha-wt-fix", @"Alpha-wt-fix\src", "Alpha2", "Alpha-wt", "Alphawt-x", "Beta", @"Beta\sub", "Other", @"Elsewhere\Alpha-wt-x", "Outside" })
			Directory.CreateDirectory(Path.Combine(Proj, dir));
		MakeJunction(Path.Combine(Alpha, "link"), Path.Combine(Proj, "Outside"));

		Settings = new AppSettingsService(Root);
		Registry = new SessionRegistry(Settings, new FakeSessionFileService(), new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
		var alpha = Registry.AddDirectory(Alpha);
		var beta  = Registry.AddDirectory(Beta);

		Lead  = Registry.AddSession(alpha, "Lead");
		Lead.WorkingDirectory = Alpha;
		Wrk   = Registry.AddSession(alpha, "Wrk");
		Wrk.WorkingDirectory    = Path.Combine(Proj, "Alpha-wt-fix");
		Wrk.SupervisorNodeId    = Lead.NodeId;
		Wrk.OrchestrationDepth  = 1;
		Other = Registry.AddSession(beta, "Other");
		Other.WorkingDirectory = Beta;

		Attention = new AttentionService(new AttentionLedgerStore(Path.Combine(Root, "attention")), AttentionFiles, Clock);
		Service   = NewService();

		Server = new AgentMcpServer(
			Settings,
			new Lazy<ISessionTurnService>(() => throw new NotSupportedException()),
			new Lazy<ISchedulerService>(() => Scheduler),
			Registry, new FakeSessionFileService(),
			new ScriptedModelService(), tdcpServer: null!, configMerger: null!,
			new FakeTelegramBridge(), new FakeNotificationService(), new FakeDirectoryLabelService(),
			Attention, TagTestEnv.Unused(),
			clock: Clock, elevatedRuns: Service);
	}

	/// <summary>A new service over the same run folders: a daemon restart. The old one stops.</summary>
	public ElevatedRunService NewService(bool? isElevated = null)
	{
		Service?.Stop();
		Service = new ElevatedRunService(
			Settings, Registry, Attention, new Lazy<ISchedulerService>(() => Scheduler), Channels,
			RunsRoot, Clock, isElevated ?? _isElevated, pid => AlivePids.Contains(pid), CreateLogger());
		return Service;
	}

	private ILogger CreateLogger() => new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(Sink).CreateLogger();

	public ElevatedRunRequestModel Req(string command = "echo hi", string? workingDir = null, string reason = "because", JsonNode? timeout = null) => new()
	{
		Command = command, WorkingDir = workingDir ?? Alpha, Reason = reason, TimeoutMinutes = timeout,
	};

	public async Task<ElevatedRunModel> RequestAsync(SessionNodeModel? caller = null, string command = "echo hi", string? workingDir = null, string reason = "because")
	{
		var outcome = await Service.RequestAsync(caller ?? Lead, Req(command, workingDir, reason));
		return outcome.Run;
	}

	/// <summary>The tool error text of a refused request.</summary>
	public async Task<string> RefusedAsync(SessionNodeModel caller, ElevatedRunRequestModel request) =>
		(await Assert.ThrowsAsync<McpToolException>(() => Service.RequestAsync(caller, request))).Message;

	public string RunDir(string id) => Path.Combine(RunsRoot, id);

	public IEnumerable<string> RunFolders() => Directory.Exists(RunsRoot) ? Directory.EnumerateDirectories(RunsRoot) : [];

	/// <summary>Plays the wrapper: writes <c>started.json</c> as the real one does.</summary>
	public void WriteStarted(string id, int pid = 4242, DateTimeOffset? at = null)
	{
		File.WriteAllText(Path.Combine(RunDir(id), "started.json"),
			$$"""{"pid":{{pid}},"commandPid":{{pid + 1}},"startedUtc":"{{(at ?? Clock.GetUtcNow()):o}}"}""");
		AlivePids.Add(pid);
	}

	/// <summary>Plays the wrapper: writes <c>result.json</c> as the real one does.</summary>
	public void WriteResult(string id, int exitCode = 0, string ended = "exited", DateTimeOffset? at = null) =>
		File.WriteAllText(Path.Combine(RunDir(id), "result.json"),
			$$"""{"exitCode":{{exitCode}},"endedUtc":"{{(at ?? Clock.GetUtcNow()):o}}","ended":"{{ended}}"}""");

	/// <summary>Approve and a client's report of the launch.</summary>
	public ElevatedRunLaunchModel Launch(string id)
	{
		var launch = Service.Approve(id);
		Service.ReportLaunched(id);
		return launch;
	}

	/// <summary>Takes a run to <c>running</c> as the real flow does: approve, launched, started.json, one poll.</summary>
	public void MakeRunning(string id, int pid = 4242)
	{
		Launch(id);
		WriteStarted(id, pid);
		Service.Poll();
	}

	public static void MakeJunction(string link, string target)
	{
		using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
		process.StandardOutput.ReadToEnd();
		process.WaitForExit();
	}

	public void Dispose()
	{
		Service.Stop();
		try
		{
			var link = Path.Combine(Alpha, "link");
			if (Directory.Exists(link))
				Directory.Delete(link); // removes the junction only
			Directory.Delete(Root, recursive: true);
		}
		catch (IOException) { }
		catch (UnauthorizedAccessException) { }
	}
}

/// <summary>Records every message the user would be notified with.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeUserChannels : IUserMessageChannels
{
	public List<(SessionNodeModel Node, string Message)> Sent { get; } = [];

	public Task<IReadOnlyList<string>> NotifyAsync(SessionNodeModel node, string message)
	{
		lock (Sent)
			Sent.Add((node, message));
		return Task.FromResult<IReadOnlyList<string>>(["Telegram", "desktop"]);
	}
}

/// <summary>A Serilog sink that keeps the rendered lines, for the audit-line tests.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class CollectingSink : ILogEventSink
{
	private readonly List<LogEvent> _events = [];

	public void Emit(LogEvent logEvent)
	{
		lock (_events)
			_events.Add(logEvent);
	}

	public IReadOnlyList<string> Lines(LogEventLevel level)
	{
		lock (_events)
			return _events.Where(e => e.Level == level).Select(e => e.RenderMessage()).ToList();
	}
}
