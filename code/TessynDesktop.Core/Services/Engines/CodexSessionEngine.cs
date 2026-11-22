using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Codex;
using TessynDesktop.Models;
using TessynDesktop.Models.Engines;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <summary>
/// The Codex CLI's app-server behind the session-engine seam (FR.33): GPT sessions on the ChatGPT plan. A session's conversation is one Codex
/// thread (FR.33.7) on the shared host of its Codex profile (FR.33.6, <see cref="CodexHostPool"/>).
/// <list type="bullet">
/// <item><b>A turn</b> starts the thread on the first turn (<c>thread/start</c>, the id stored before the first event), resumes it once per host
/// process (<c>thread/resume</c>), then sends <c>turn/start</c> and reports <see cref="CodexTurnMapper"/>'s events to the sink (FR.33.11).</item>
/// <item><b>Resume errors</b> (FR.33.7): a gone thread is detached and a new one is seeded from the transcript; an active writer ends the turn
/// with its SYSTEM text and keeps the thread; a thread no longer loaded is resumed once and the turn retried.</item>
/// <item><b>Stop</b> (FR.33.8) is the turn's cancellation: <c>turn/interrupt</c>, then up to 15 s for <c>interrupted</c>; without it the turn
/// ends as cancelled with a SYSTEM entry and the thread is unsubscribed. The shared host is never killed for it.</item>
/// <item><b><see cref="IsAlive"/> is always false.</b> A Codex session has no process of its own: "alive" drives the FR.19.13 redirect of
/// headless turns into a live process and the Streaming state of the UI, and neither applies (FR.33.6; FR.19 does not apply to Codex). Every
/// Codex turn goes through <see cref="RunTurnAsync"/>, serialised by the caller's turn lock.</item>
/// <item><b>Mid-run corrections</b> are queued and sent with the next turn (FR.33.12): there is no stateless side channel.</item>
/// <item><b>Compaction</b> is native (<see cref="INativeCompactionEngine"/>, <c>thread/compact/start</c>, FR.33.12).</item>
/// </list>
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class CodexSessionEngine : ISessionEngine, INativeCompactionEngine
{
	private static readonly ILogger _log = Log.ForContext<CodexSessionEngine>();

	private static readonly EngineCapabilities CodexCapabilities = new(
		PersistentProcess: false,
		OneShotProcess:    false,
		InBandInterrupt:   true,
		ReportsCost:       false,
		NativeCompaction:  true,
		Chrome:            false,
		LocalModels:       false,
		MidTurnCorrection: false);

	private readonly CodexHostPool _pool;
	private readonly CodexThreadConfigBuilder _config;
	private readonly ISessionFileService _files;
	private readonly IAppSettingsService _appSettings;
	private readonly ISessionRegistry? _registry;
	private readonly ICodexProfileSelector _profiles;
	private readonly TimeProvider _time;

	// The node's turn now running (Stop, InterruptAsync); one per node, because the caller's turn lock serialises a node's turns.
	private readonly ConcurrentDictionary<string, RunningTurn> _running = new(StringComparer.Ordinal);

	// Mid-run corrections waiting for the node's next turn (FR.33.12), in the order they came.
	private readonly ConcurrentDictionary<string, List<string>> _corrections = new(StringComparer.Ordinal);

	// The name each thread was last given with thread/name/set (FR.33.7), and the session it belongs to (for renames).
	private readonly ConcurrentDictionary<string, NamedThread> _threadNames = new(StringComparer.Ordinal);
	private int _renameWatch;

	public CodexSessionEngine(
		CodexHostPool pool,
		CodexThreadConfigBuilder config,
		ISessionFileService files,
		IAppSettingsService appSettings,
		ISessionRegistry? registry = null,
		ICodexProfileSelector? profiles = null,
		TimeProvider? time = null)
	{
		_pool        = pool;
		_config      = config;
		_files       = files;
		_appSettings = appSettings;
		_registry    = registry;
		_profiles    = profiles ?? new CodexDefaultProfileSelector();
		_time        = time ?? TimeProvider.System;
	}

	/// <inheritdoc />
	public string EngineId => Constants.SessionEngines.Codex;

	/// <inheritdoc />
	public string DisplayName => Constants.SessionEngines.CodexDisplayName;

	/// <inheritdoc />
	public EngineCapabilities Capabilities => CodexCapabilities;

	/// <inheritdoc />
	public string? GetConversationId(SessionNodeModel node) => node.CodexThreadId;

	/// <inheritdoc />
	public void ClearConversation(SessionNodeModel node)
	{
		lock (_appSettings.SyncRoot)
		{
			node.CodexThreadId = null;
			_appSettings.Save();
		}
	}

	/// <summary>Moves the thread id to <see cref="SessionNodeModel.PriorCodexThreadIds"/> (FR.33.12, FR.33.4) and lets the host forget the thread.</summary>
	public bool DetachConversation(SessionNodeModel node)
	{
		string? detached;
		lock (_appSettings.SyncRoot)
		{
			detached = node.CodexThreadId;
			if (string.IsNullOrEmpty(detached))
				return false;

			(node.PriorCodexThreadIds ??= []).Add(detached);
			node.CodexThreadId = null;
			_appSettings.Save();
		}

		_log.Information("Detached Codex thread {ThreadId} of session {NodeId}", detached, node.NodeId);
		_threadNames.TryRemove(detached, out _);
		_ = UnsubscribeQuietlyAsync(node, detached);
		return true;
	}

	/// <summary>Codex threads are not moved between profiles (FR.33.5): there is nothing to adopt, so a thread is always resumable as it is.</summary>
	public bool AdoptConversation(SessionNodeModel node, string conversationId, string? profileConfigDir) => true;

	/// <summary>The thread's config (FR.33.9), built before the turn lock so the registry's OAuth tokens are refreshed outside it.</summary>
	public async Task<EngineTurnPreparation?> PrepareTurnAsync(SessionNodeModel node, TurnSettings settings) =>
		new CodexTurnPreparation(await _config.BuildAsync(node).ConfigureAwait(false));

	/// <summary>Never called: <see cref="IsAlive"/> is always false, so no headless turn is redirected into a live process (FR.19.13).</summary>
	public Task RunTurnInLiveProcessAsync(SessionNodeModel node, string prompt, IEngineEventSink sink, CancellationToken cancellationToken) =>
		throw new NotSupportedException("A Codex session has no live process of its own; its turns run through RunTurnAsync.");

	/// <summary>Queues the correction for the session's next turn (FR.33.12). Nothing is sent now.</summary>
	public void SendCorrection(SessionNodeModel node, TurnSettings settings, string prompt)
	{
		var queue = _corrections.GetOrAdd(node.NodeId, _ => []);
		lock (queue)
			queue.Add(prompt);
		_log.Information("Mid-run correction for Codex session {NodeId} queued for its next turn", node.NodeId);
	}

	/// <summary>Stops the session's running turn in-band (<c>turn/interrupt</c>, FR.33.8). True when a turn was running.</summary>
	public Task<bool> InterruptAsync(string nodeId) => Task.FromResult(CancelRunning(nodeId));

	/// <summary>Always false: see the class remarks.</summary>
	public bool IsAlive(string nodeId) => false;

	/// <summary>Stops the session's running turn, if any (the same as <see cref="InterruptAsync"/>). The shared host stays up.</summary>
	public void Stop(string nodeId) => CancelRunning(nodeId);

	/// <summary>Kills every Codex host ("Terminate all", FR.15.5, daemon exit).</summary>
	public void TerminateAll() => _pool.TerminateAll();

	private bool CancelRunning(string nodeId)
	{
		if (!_running.TryGetValue(nodeId, out var running))
			return false;

		running.Cancel();
		return true;
	}

	/// <summary>The queued corrections (FR.33.12) as extra instruction lines of the next turn; empty when there are none. Taking them clears them.</summary>
	private string TakeCorrections(string nodeId)
	{
		if (!_corrections.TryGetValue(nodeId, out var queue))
			return string.Empty;

		List<string> taken;
		lock (queue)
		{
			taken = queue.ToList();
			queue.Clear();
		}

		return taken.Count == 0 ? string.Empty : "\n" + string.Join("\n", taken.Select(c => "- " + c));
	}

	/// <summary>Lets the host forget a detached thread, so it can idle out and the User's CLI can open it (FR.33.6). Failures are only logged.</summary>
	private async Task UnsubscribeQuietlyAsync(SessionNodeModel node, string threadId)
	{
		try
		{
			var host = _pool.Hosts.FirstOrDefault(h => h.IsThreadLoaded(threadId));
			if (host is not null)
				await host.UnsubscribeThreadAsync(threadId).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Unsubscribing Codex thread {ThreadId} of session {NodeId} failed", threadId, node.NodeId);
		}
	}

	/// <summary>A thread named by this engine in this run: its session, its profile and the name last set.</summary>
	private sealed record NamedThread(SessionNodeModel Node, CodexProfileRef Profile, string Name);
}
