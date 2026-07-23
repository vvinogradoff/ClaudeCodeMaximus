using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <summary>
/// The single boundary between the Avalonia UI and the daemon-owned domain (Stage 4 of the
/// UI/daemon split). Today an in-process adapter (<see cref="InProcDaemonClient"/>) delegates to
/// Core services; after Stage 5 a wire adapter (WireDaemonClient) will implement this same surface
/// over TDCP (the Tessyn Daemon Control Protocol). ViewModels are migrated to depend only on this
/// interface so that switching transport later touches no ViewModel.
/// </summary>
/// <remarks>
/// First cut covers the daemon-owned operations the UI's core turn / tree / schedule / content
/// flows require. Tree CRUD, model/effort/profile selection and usage fold in as their consumers
/// migrate (Stages 6–7). See docs/refactoring/tdcp-spec.md for the wire-method mapping.
/// Created by Claude.
/// </remarks>
public interface IDaemonClient
{
	// ── Change notification (daemon → UI) ──────────────────────────────────────

	/// <summary>Raised when the session tree changes (session added, running-state, settings saved, reload).</summary>
	event EventHandler<TreeChangedEventArgs> TreeChanged;

	/// <summary>Raised when a schedule is added, removed, or fires. Used to refresh session UI.</summary>
	event EventHandler? ScheduleChanged;

	// ── Tree read ───────────────────────────────────────────────────────────────

	/// <summary>The authoritative directory list (source of truth for the tree projection).</summary>
	IReadOnlyList<DirectoryNodeModel> Directories { get; }

	/// <summary>NodeId of the currently selected session in the UI, or null.</summary>
	string? SelectedNodeId { get; set; }

	SessionNodeModel? FindSessionByNodeId(string nodeId);
	SessionNodeModel? FindSessionByAgentToken(string token);

	/// <summary>Resolves the (directory label, tree path) location of a session for header display.</summary>
	(string directoryLabel, string treePath) GetSessionLocation(SessionNodeModel session);

	// ── Running state ────────────────────────────────────────────────────────────

	/// <summary>Returns true while a claude process is running for the given node.</summary>
	bool IsSessionRunning(string nodeId);

	/// <summary>Marks the session as running or not. Thread-safe. Raises <see cref="TreeChanged"/>.</summary>
	void SetSessionRunning(string nodeId, bool isRunning);

	// ── Turns ────────────────────────────────────────────────────────────────────

	/// <summary>Acquires (or creates) the exclusive per-node turn lock (at most one turn per NodeId).</summary>
	SemaphoreSlim GetTurnLock(string nodeId);

	/// <summary>Runs a single headless turn against <paramref name="node"/> (scheduler / orchestration / UI).</summary>
	Task<TurnResultModel> RunTurnAsync(
		SessionNodeModel node,
		string prompt,
		TurnSource source,
		CancellationToken cancellationToken = default);

	/// <summary>Cancels any running turn for the given node. Returns true if a running turn was cancelled.</summary>
	bool CancelTurn(string nodeId);

	/// <summary>Resolves the profile display name a turn against <paramref name="node"/> would use (inheritance).</summary>
	string? ResolveProfileName(SessionNodeModel node);

	// ── Schedules ────────────────────────────────────────────────────────────────

	/// <summary>Returns all schedules, or only those targeting <paramref name="targetNodeId"/> when non-null.</summary>
	IReadOnlyList<ScheduleModel> GetSchedules(string? targetNodeId = null);

	/// <summary>Adds a schedule, persists immediately, and wakes the timer if needed.</summary>
	void AddSchedule(ScheduleModel schedule);

	/// <summary>Removes a schedule by ID. Returns true if found and removed.</summary>
	bool RemoveSchedule(string scheduleId);

	// ── Session content (read) ─────────────────────────────────────────────────────

	/// <summary>Reads the parsed message entries of a session file.</summary>
	IReadOnlyList<SessionEntryModel> ReadEntries(string fileName);

	/// <summary>Returns true if the session .txt file exists.</summary>
	bool SessionFileExists(string fileName);

	/// <summary>Returns the full filesystem path for a session file name.</summary>
	string GetSessionFilePath(string fileName);
}
