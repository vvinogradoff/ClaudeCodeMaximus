using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Agent;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Implements <see cref="IDaemonClient"/> over the TDCP wire (JSON-RPC 2.0 over Streamable HTTP).
/// Each request/response member POSTs a JSON-RPC request to <c>/tdcp</c> and deserialises the result.
/// A background task holds the SSE GET stream and re-raises <see cref="TreeChanged"/>/<see cref="ScheduleChanged"/>
/// from incoming push notifications.
/// </summary>
/// <remarks>
/// <b>GetTurnLock</b> throws <see cref="NotSupportedException"/> — the per-node turn lock lives
/// daemon-side; the UI acquires it indirectly by calling <c>turn.run</c>.
/// <b>Directories</b> are fetched on first access and cached until a <c>tree.changed</c>
/// SSE notification invalidates the cache (see property documentation).
/// <b>LoadFromSettings and the Stage 5b tree-CRUD members</b> also throw
/// <see cref="NotSupportedException"/> — not yet wired over TDCP (see tdcp-spec.md §5); only
/// <see cref="InProcDaemonClient"/> implements them for real today.
/// Created by Claude.
/// </remarks>
public sealed class WireDaemonClient : IDaemonClient, IDisposable
{
	private static readonly ILogger _log = Log.ForContext<WireDaemonClient>();

	private readonly HttpClient              _http;
	private readonly string                  _tdcpUrl;
	private readonly string                  _token;
	private          CancellationTokenSource? _cts;
	private          Task?                    _sseTask;

	// Cached after first tree.list call; invalidated by tree.changed SSE notifications.
	private IReadOnlyList<DirectoryNodeModel>? _cachedDirectories;

	public WireDaemonClient(string baseAddress, string token)
	{
		_token   = token;
		_tdcpUrl = baseAddress.TrimEnd('/') + Constants.Agent.TdcpEndpointPath;
		_http    = new HttpClient();
	}

	// ── Lifecycle ─────────────────────────────────────────────────────────────────

	/// <summary>Starts the background SSE stream for push notifications.</summary>
	public Task StartAsync()
	{
		_cts     = new CancellationTokenSource();
		_sseTask = RunSseLoopAsync(_cts.Token);
		return Task.CompletedTask;
	}

	/// <summary>Cancels the SSE stream and disposes resources.</summary>
	public void Stop()
	{
		_cts?.Cancel();
	}

	/// <inheritdoc />
	public void Dispose()
	{
		Stop();
		_http.Dispose();
	}

	// ── Change notification ───────────────────────────────────────────────────────

	public event EventHandler<TreeChangedEventArgs>? TreeChanged;
	public event EventHandler?                       ScheduleChanged;

	/// <summary>
	/// Not yet wired over TDCP — <c>content.appended</c> push is specified (tdcp-spec.md §5) but not
	/// implemented; only <see cref="InProcDaemonClient"/> relays it today. Subscribing is a silent
	/// no-op rather than a throw since it's an event, not a call the caller can wrap in a try/catch
	/// at the point of failure.
	/// </summary>
#pragma warning disable CS0067 // Never raised until content.appended SSE push exists — see doc comment.
	public event EventHandler<SessionEntryAppendedEventArgs>? EntryAppended;
#pragma warning restore CS0067

	// ── Tree read ─────────────────────────────────────────────────────────────────

	/// <summary>
	/// Fetched lazily via <c>tree.list</c> and cached until the next <c>tree.changed</c> SSE
	/// notification invalidates the cache. The blocking call is acceptable because the interface
	/// exposes Directories as a synchronous property; the in-proc adapter also returns synchronously.
	/// </summary>
	public IReadOnlyList<DirectoryNodeModel> Directories
	{
		get
		{
			if (_cachedDirectories != null)
				return _cachedDirectories;

			var result = CallAsync<List<DirectoryNodeModel>>("tree.list")
				.GetAwaiter().GetResult();
			_cachedDirectories = result ?? new List<DirectoryNodeModel>();
			return _cachedDirectories;
		}
	}

	public string? SelectedNodeId
	{
		get
		{
			var result = CallAsync<SelectedNodeIdResult>("tree.getSelected")
				.GetAwaiter().GetResult();
			return result?.NodeId;
		}
		set
		{
			var p = new JsonObject();
			if (value != null)
				p["nodeId"] = value;
			CallAsync("tree.setSelected", p).GetAwaiter().GetResult();
		}
	}

	public SessionNodeModel? FindSessionByNodeId(string nodeId)
	{
		var p = new JsonObject { ["nodeId"] = nodeId };
		return CallAsync<SessionNodeModel>("tree.findByNodeId", p).GetAwaiter().GetResult();
	}

	public SessionNodeModel? FindSessionByAgentToken(string token)
	{
		var p = new JsonObject { ["token"] = token };
		return CallAsync<SessionNodeModel>("tree.findByToken", p).GetAwaiter().GetResult();
	}

	/// <summary>Not available over the TDCP wire yet — no <c>tree.findByFileName</c> method is specified; see tdcp-spec.md §5.</summary>
	public SessionNodeModel? FindSessionByFileName(string fileName) =>
		throw new NotSupportedException(
			"FindSessionByFileName is not available over the TDCP wire yet; see tdcp-spec.md §5.");

	public (string directoryLabel, string treePath) GetSessionLocation(SessionNodeModel session)
	{
		var p      = new JsonObject { ["nodeId"] = session.NodeId };
		var result = CallAsync<LocationResult>("tree.location", p).GetAwaiter().GetResult()
		             ?? throw new InvalidOperationException($"tree.location returned null for node {session.NodeId}");
		return (result.DirectoryLabel, result.TreePath);
	}

	// ── Running state ─────────────────────────────────────────────────────────────

	public bool IsSessionRunning(string nodeId)
	{
		var p      = new JsonObject { ["nodeId"] = nodeId };
		var result = CallAsync<IsRunningResult>("turn.isRunning", p).GetAwaiter().GetResult();
		return result?.Running ?? false;
	}

	public void SetSessionRunning(string nodeId, bool isRunning)
	{
		var p = new JsonObject { ["nodeId"] = nodeId, ["running"] = isRunning };
		CallAsync("turn.setRunning", p).GetAwaiter().GetResult();
	}

	// ── Turns ─────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Not supported over the wire — the per-node turn lock lives daemon-side.
	/// The UI acquires concurrency protection indirectly by calling turn.run,
	/// which the daemon serialises internally per session.
	/// </summary>
	public SemaphoreSlim GetTurnLock(string nodeId) =>
		throw new NotSupportedException(
			"GetTurnLock is not available over the TDCP wire. " +
			"The per-node turn lock lives daemon-side; use RunTurnAsync instead.");

	/// <summary>
	/// Runs a turn. Passes <paramref name="node"/>.<see cref="SessionNodeModel.NodeId"/> over the
	/// wire (per TDCP §4); the daemon resolves the node from its registry.
	/// </summary>
	public async Task<TurnResultModel> RunTurnAsync(
		SessionNodeModel      node,
		string                prompt,
		TurnSource            source,
		CancellationToken     cancellationToken = default)
	{
		var p = new JsonObject
		{
			["nodeId"]  = node.NodeId,
			["prompt"]  = prompt,
			["source"]  = source.ToString(),
		};
		var result = await CallAsync<TurnResultModel>("turn.run", p);
		return result ?? new TurnResultModel(AssistantText: string.Empty, SessionId: null, IsError: true, ErrorMessage: "Empty response from server");
	}

	public bool CancelTurn(string nodeId)
	{
		var p      = new JsonObject { ["nodeId"] = nodeId };
		var result = CallAsync<CancelledResult>("turn.cancel", p).GetAwaiter().GetResult();
		return result?.Cancelled ?? false;
	}

	public string? ResolveProfileName(SessionNodeModel node)
	{
		var p      = new JsonObject { ["nodeId"] = node.NodeId };
		var result = CallAsync<ProfileNameResult>("turn.resolveProfile", p).GetAwaiter().GetResult();
		return result?.ProfileName;
	}

	// ── Schedules ─────────────────────────────────────────────────────────────────

	public IReadOnlyList<ScheduleModel> GetSchedules(string? targetNodeId = null)
	{
		var p = new JsonObject();
		if (targetNodeId != null)
			p["targetNodeId"] = targetNodeId;
		var result = CallAsync<List<ScheduleModel>>("schedule.list", p).GetAwaiter().GetResult();
		return result ?? new List<ScheduleModel>();
	}

	public void AddSchedule(ScheduleModel schedule)
	{
		var scheduleNode = JsonNode.Parse(JsonSerializer.Serialize(schedule, TdcpServer.JsonOpts))!;
		CallAsync("schedule.add", scheduleNode).GetAwaiter().GetResult();
	}

	public bool RemoveSchedule(string scheduleId)
	{
		var p      = new JsonObject { ["scheduleId"] = scheduleId };
		var result = CallAsync<RemovedResult>("schedule.remove", p).GetAwaiter().GetResult();
		return result?.Removed ?? false;
	}

	// ── Session content ───────────────────────────────────────────────────────────

	public IReadOnlyList<SessionEntryModel> ReadEntries(string fileName)
	{
		var p      = new JsonObject { ["fileName"] = fileName };
		var result = CallAsync<List<SessionEntryModel>>("content.read", p).GetAwaiter().GetResult();
		return result ?? new List<SessionEntryModel>();
	}

	public bool SessionFileExists(string fileName)
	{
		var p      = new JsonObject { ["fileName"] = fileName };
		var result = CallAsync<ExistsResult>("content.exists", p).GetAwaiter().GetResult();
		return result?.Exists ?? false;
	}

	public string GetSessionFilePath(string fileName)
	{
		var p      = new JsonObject { ["fileName"] = fileName };
		var result = CallAsync<FilePathResult>("content.path", p).GetAwaiter().GetResult();
		return result?.Path ?? string.Empty;
	}

	// ── Initialization ─────────────────────────────────────────────────────────────

	/// <summary>
	/// Not supported over the wire — startup-only, called once in App.axaml.cs after
	/// appSettings.Load(); wouldn't make sense for a remote client to call regardless.
	/// </summary>
	public void LoadFromSettings() =>
		throw new NotSupportedException(
			"LoadFromSettings is not available over the TDCP wire. " +
			"It is a daemon-side startup operation only.");

	// ── CRUD – Directory ─────────────────────────────────────────────────────────────

	public DirectoryNodeModel AddDirectory(string path) =>
		throw new NotSupportedException(
			"AddDirectory is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	public bool TryDeleteDirectory(DirectoryNodeModel directory) =>
		throw new NotSupportedException(
			"TryDeleteDirectory is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	// ── CRUD – Group ─────────────────────────────────────────────────────────────────

	public GroupNodeModel AddGroup(DirectoryNodeModel parent, string name) =>
		throw new NotSupportedException(
			"AddGroup is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	public GroupNodeModel AddGroupToGroup(GroupNodeModel parent, string name) =>
		throw new NotSupportedException(
			"AddGroupToGroup is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	public bool TryDeleteGroup(DirectoryNodeModel parent, GroupNodeModel group) =>
		throw new NotSupportedException(
			"TryDeleteGroup is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	public bool TryDeleteGroupFromGroup(GroupNodeModel parent, GroupNodeModel group) =>
		throw new NotSupportedException(
			"TryDeleteGroupFromGroup is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	public void RenameGroup(GroupNodeModel group, string newName) =>
		throw new NotSupportedException(
			"RenameGroup is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	// ── CRUD – Session ───────────────────────────────────────────────────────────────

	public SessionNodeModel AddSession(DirectoryNodeModel parent, string name) =>
		throw new NotSupportedException(
			"AddSession is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	public SessionNodeModel AddSessionToGroup(GroupNodeModel parent, string name) =>
		throw new NotSupportedException(
			"AddSessionToGroup is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	public SessionNodeModel ImportSession(DirectoryNodeModel parent, string name, string fileName, string claudeSessionId) =>
		throw new NotSupportedException(
			"ImportSession is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	public SessionNodeModel ImportSessionToGroup(GroupNodeModel parent, string name, string fileName, string claudeSessionId) =>
		throw new NotSupportedException(
			"ImportSessionToGroup is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	public bool TryDeleteSession(DirectoryNodeModel parent, SessionNodeModel session, bool forceDelete = false) =>
		throw new NotSupportedException(
			"TryDeleteSession is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	public bool TryDeleteSessionFromGroup(GroupNodeModel parent, SessionNodeModel session, bool forceDelete = false) =>
		throw new NotSupportedException(
			"TryDeleteSessionFromGroup is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	public void RenameSession(SessionNodeModel session, string newName) =>
		throw new NotSupportedException(
			"RenameSession is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	// ── Model-level move ─────────────────────────────────────────────────────────────

	public DirectoryNodeModel? RemoveSessionFromModel(SessionNodeModel session) =>
		throw new NotSupportedException(
			"RemoveSessionFromModel is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	public void InsertSessionIntoDirectory(DirectoryNodeModel parent, SessionNodeModel session, int index) =>
		throw new NotSupportedException(
			"InsertSessionIntoDirectory is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	public void InsertSessionIntoGroup(GroupNodeModel parent, SessionNodeModel session, int index) =>
		throw new NotSupportedException(
			"InsertSessionIntoGroup is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	// ── Orchestration helpers ────────────────────────────────────────────────────────

	public List<SessionSummaryModel> BuildAgentSessionSummaries() =>
		throw new NotSupportedException(
			"BuildAgentSessionSummaries is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	public IReadOnlySet<string> CollectAllClaudeSessionIds() =>
		throw new NotSupportedException(
			"CollectAllClaudeSessionIds is not available over the TDCP wire yet. " +
			"Tree CRUD is daemon-side only for now; see tdcp-spec.md §5.");

	// ── Cross-project search & titles ────────────────────────────────────────────────

	public Task<int> ReindexSessionsAsync(CancellationToken cancellationToken = default) =>
		throw new NotSupportedException(
			"ReindexSessionsAsync is not available over the TDCP wire yet; see tdcp-spec.md §5.");

	public Task<IReadOnlyList<SessionSearchResultModel>> SearchSessionsAsync(
		string query, string? projectSlug = null, string? role = null,
		int? limit = null, int? offset = null, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException(
			"SearchSessionsAsync is not available over the TDCP wire yet; see tdcp-spec.md §5.");

	public Task<IReadOnlySet<string>> SearchMatchingSessionIdsAsync(string query, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException(
			"SearchMatchingSessionIdsAsync is not available over the TDCP wire yet; see tdcp-spec.md §5.");

	public Task<IReadOnlyDictionary<string, string>> EnsureTitlesAsync(
		IReadOnlyList<ClaudeSessionSummaryModel> summaries,
		Action<IReadOnlyDictionary<string, string>>? onBatchComplete = null,
		CancellationToken cancellationToken = default) =>
		throw new NotSupportedException(
			"EnsureTitlesAsync is not available over the TDCP wire yet; see tdcp-spec.md §5.");

	public Task<IReadOnlyList<ClaudeSessionSummaryModel>> GetSessionSummariesAsync(
		IReadOnlyList<string> externalIds, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException(
			"GetSessionSummariesAsync is not available over the TDCP wire yet; see tdcp-spec.md §5.");

	// ── SSE background loop ───────────────────────────────────────────────────────

	private async Task RunSseLoopAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			try
			{
				using var request = new HttpRequestMessage(HttpMethod.Get, _tdcpUrl);
				request.Headers.Add("Accept", "text/event-stream");
				request.Headers.Add(Constants.Agent.TokenHeader, _token);

				using var response = await _http.SendAsync(
					request, HttpCompletionOption.ResponseHeadersRead, ct);
				response.EnsureSuccessStatusCode();

				using var stream = await response.Content.ReadAsStreamAsync(ct);
				using var reader = new StreamReader(stream, Encoding.UTF8);

				while (!ct.IsCancellationRequested)
				{
					var line = await reader.ReadLineAsync(ct);
					if (line == null)
						break; // stream ended (server closed)

					if (!line.StartsWith("data: ", StringComparison.Ordinal))
						continue;

					var json = line["data: ".Length..];
					ProcessSseFrame(json);
				}
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				if (!ct.IsCancellationRequested)
				{
					_log.Debug(ex, "WireDaemonClient: SSE disconnected, reconnecting in 1 s");
					try { await Task.Delay(TimeSpan.FromSeconds(1), ct); }
					catch (OperationCanceledException) { break; }
				}
			}
		}
	}

	private void ProcessSseFrame(string json)
	{
		try
		{
			var notification = JsonNode.Parse(json);
			var method       = notification?["method"]?.GetValue<string>();

			switch (method)
			{
				case "tree.changed":
					var paramsJson = notification?["params"]?.ToJsonString() ?? "{}";
					var args = JsonSerializer.Deserialize<TreeChangedEventArgs>(paramsJson, TdcpServer.JsonOpts);
					if (args != null)
					{
						_cachedDirectories = null; // Invalidate cache on any tree change.
						TreeChanged?.Invoke(this, args);
					}
					break;
				case "schedule.changed":
					ScheduleChanged?.Invoke(this, EventArgs.Empty);
					break;
				default:
					_log.Debug("WireDaemonClient: unknown SSE notification method: {Method}", method);
					break;
			}
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "WireDaemonClient: error processing SSE frame");
		}
	}

	// ── JSON-RPC helpers ──────────────────────────────────────────────────────────

	private async Task<JsonNode?> CallAsync(string method, JsonNode? @params = null)
	{
		var request = new JsonObject
		{
			["jsonrpc"] = "2.0",
			["id"]      = 1,
			["method"]  = method,
		};
		if (@params != null)
			request["params"] = @params.DeepClone();

		var body    = request.ToJsonString();
		var content = new StringContent(body, Encoding.UTF8, "application/json");

		using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _tdcpUrl)
		{
			Content = content,
		};
		httpRequest.Headers.Add(Constants.Agent.TokenHeader, _token);

		using var response = await _http.SendAsync(httpRequest);
		var responseJson = await response.Content.ReadAsStringAsync();

		var responseNode = JsonNode.Parse(responseJson);
		if (responseNode?["error"] is not null)
		{
			var msg = responseNode["error"]?["message"]?.GetValue<string>() ?? "Unknown TDCP error";
			throw new TdcpCallException(msg);
		}

		// Use ContainsKey to distinguish "result: null" (valid JSON null) from absent "result" field.
		if (responseNode is not JsonObject obj || !obj.ContainsKey("result"))
			throw new TdcpCallException("No result field in TDCP response");

		return responseNode["result"]; // null when result is JSON null (e.g. session not found)
	}

	private async Task<T?> CallAsync<T>(string method, JsonNode? @params = null)
	{
		var result = await CallAsync(method, @params);
		if (result == null)
			return default; // JSON null → C# null (e.g. optional session lookup)
		return JsonSerializer.Deserialize<T>(result.ToJsonString(), TdcpServer.JsonOpts);
	}

	// ── Private deserialization records ───────────────────────────────────────────
	// Mirrored from TdcpServer's result records; only used within WireDaemonClient.

	private sealed record SelectedNodeIdResult(string? NodeId);
	private sealed record LocationResult(string DirectoryLabel, string TreePath);
	private sealed record IsRunningResult(bool Running);
	private sealed record CancelledResult(bool Cancelled);
	private sealed record ProfileNameResult(string? ProfileName);
	private sealed record RemovedResult(bool Removed);
	private sealed record ExistsResult(bool Exists);
	private sealed record FilePathResult(string Path);
}

/// <summary>Thrown when the TDCP server returns a JSON-RPC error response.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TdcpCallException(string message) : Exception(message);
