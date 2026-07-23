using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <summary>
/// In-process implementation of <see cref="IDaemonClient"/>: delegates directly to the Core
/// services running in the same process. This is the Stage 4 adapter that lets ViewModels move
/// onto the daemon boundary before the daemon is a separate process. In Stage 5 a WireDaemonClient
/// implementing the same interface over TDCP replaces this one, with no ViewModel changes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class InProcDaemonClient : IDaemonClient
{
	private readonly ISessionRegistry _registry;
	private readonly ISessionTurnService _turnService;
	private readonly ISchedulerService _scheduler;
	private readonly ISessionFileService _fileService;

	public InProcDaemonClient(
		ISessionRegistry registry,
		ISessionTurnService turnService,
		ISchedulerService scheduler,
		ISessionFileService fileService)
	{
		_registry = registry;
		_turnService = turnService;
		_scheduler = scheduler;
		_fileService = fileService;

		// Forward the underlying domain events through the boundary. Over TDCP these become
		// push notifications on the change channel; in-process they are a direct relay.
		_registry.TreeChanged += (s, e) => TreeChanged?.Invoke(this, e);
		_scheduler.ScheduleChanged += (s, e) => ScheduleChanged?.Invoke(this, e);
	}

	// ── Change notification ─────────────────────────────────────────────────────

	public event EventHandler<TreeChangedEventArgs>? TreeChanged;
	public event EventHandler? ScheduleChanged;

	// ── Tree read ─────────────────────────────────────────────────────────────────

	public IReadOnlyList<DirectoryNodeModel> Directories => _registry.Directories;

	public string? SelectedNodeId
	{
		get => _registry.SelectedNodeId;
		set => _registry.SelectedNodeId = value;
	}

	public SessionNodeModel? FindSessionByNodeId(string nodeId) => _registry.FindModelByNodeId(nodeId);

	public SessionNodeModel? FindSessionByAgentToken(string token) => _registry.FindModelByAgentToken(token);

	public (string directoryLabel, string treePath) GetSessionLocation(SessionNodeModel session)
		=> _registry.GetSessionLocation(session);

	// ── Running state ───────────────────────────────────────────────────────────

	public bool IsSessionRunning(string nodeId) => _registry.IsSessionRunning(nodeId);

	public void SetSessionRunning(string nodeId, bool isRunning) => _registry.SetSessionRunning(nodeId, isRunning);

	// ── Turns ─────────────────────────────────────────────────────────────────────

	public SemaphoreSlim GetTurnLock(string nodeId) => _turnService.GetTurnLock(nodeId);

	public Task<TurnResultModel> RunTurnAsync(
		SessionNodeModel node,
		string prompt,
		TurnSource source,
		CancellationToken cancellationToken = default)
		=> _turnService.RunTurnAsync(node, prompt, source, cancellationToken);

	public bool CancelTurn(string nodeId) => _turnService.CancelTurn(nodeId);

	public string? ResolveProfileName(SessionNodeModel node) => _turnService.ResolveProfileName(node);

	// ── Schedules ───────────────────────────────────────────────────────────────

	public IReadOnlyList<ScheduleModel> GetSchedules(string? targetNodeId = null) => _scheduler.GetSchedules(targetNodeId);

	public void AddSchedule(ScheduleModel schedule) => _scheduler.AddSchedule(schedule);

	public bool RemoveSchedule(string scheduleId) => _scheduler.RemoveSchedule(scheduleId);

	// ── Session content (read) ─────────────────────────────────────────────────────

	public IReadOnlyList<SessionEntryModel> ReadEntries(string fileName) => _fileService.ReadEntries(fileName);

	public bool SessionFileExists(string fileName) => _fileService.SessionFileExists(fileName);

	public string GetSessionFilePath(string fileName) => _fileService.GetFullPath(fileName);
}
