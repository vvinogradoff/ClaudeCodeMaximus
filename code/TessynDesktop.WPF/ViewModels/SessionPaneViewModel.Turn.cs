using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Transcript;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The turn loop of the session pane (W1a): Send and Stop go through <c>turn.send</c> / <c>turn.stop</c>, and the <c>turn.*</c> notifications
/// drive the live row below the transcript (status with timer, streamed text, tools, queued turns, errors). The live row is a preview: the
/// persisted entries arrive through <c>session.appended</c> and stay the source of truth. All state changes run on the UI thread, in the order
/// the notifications arrived; bursts of deltas are merged into one page update per <see cref="LiveFlushInterval"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel
{
	private readonly Dictionary<string, SessionTurnsState> _turnStates = new();
	private readonly Queue<string> _errorOrder = new();
	private Task _handoffTask = Task.CompletedTask;
	private bool _liveFlushScheduled;
	private bool _daemonRunning;
	private long? _genericSinceUnixMs;

	/// <summary>Counts the running-state pushes (<c>session.changed</c>, <c>turn.completed</c>) of the open session: an answer to a request that began before one of them has a stale running flag.</summary>
	private int _stateEvents;

	/// <summary>How long streamed deltas are collected before the page is updated; zero updates it at once (tests).</summary>
	public TimeSpan LiveFlushInterval { get; set; } = Constants.TurnLimits.LiveFlushInterval;

	/// <summary>The clock of the thinking timer and of the generic working row.</summary>
	public TimeProvider Clock { get; set; } = TimeProvider.System;

	/// <summary>An empty box can be sent while Auto-Compact is on: it then only compacts (FR.11.6).</summary>
	private bool CanSend() => HasSession && IsSessionReady && (!string.IsNullOrWhiteSpace(InputText) || Instruction?.AutoCompact == true);

	partial void OnHasSessionChanged(bool value) => SendCommand.NotifyCanExecuteChanged();

	[RelayCommand(CanExecute = nameof(CanSend))]
	private async Task SendAsync()
	{
		var id = NodeId;
		var text = InputText.Trim();
		if (id == null || (text.Length == 0 && Instruction?.AutoCompact != true))
			return;

		// A14: what the user picked travels with this send, so a queued send keeps it whatever is picked later. A field the user did not pick is
		// not sent (tdcp-spec 9.8): the value the selector shows came from lastUsed, and sending it would pin the session to it.
		var picks = PicksOfOpenSession();
		var oneShots = Instruction.TakeOneShots();   // New Branch and Auto-Compact go with this send and switch off

		// The input is taken before the round trip: a second press (or key repeat) finds nothing to send, and text typed meanwhile is kept.
		InputText = string.Empty;
		Error = null;
		ClearTurnError(id);
		try
		{
			var result = await _client.TurnSendAsync(id, text, picks.ModelId, picks.EffortId, picks.ProfileId, Guid.NewGuid().ToString("N"), oneShots.NewBranch, oneShots.AutoCompact);
			if (!result.Accepted)
			{
				RestoreInput(id, text);
				Instruction.Restore(id, oneShots);
				Error = Constants.TurnTexts.NotAccepted;
			}
		}
		catch (Exception ex)
		{
			RestoreInput(id, text);
			Instruction.Restore(id, oneShots);
			_logger.LogWarning(ex, "turn.send failed for {NodeId}", id);
			Error = Constants.TurnTexts.SendFailed + ex.Message;
		}
	}

	/// <summary>A send that did not happen gives its text back, unless the user already typed something else (or left the session: then it goes to the draft).</summary>
	private void RestoreInput(string nodeId, string text)
	{
		if (nodeId == NodeId)
		{
			if (InputText.Length == 0)
				InputText = text;

			return;
		}

		if (string.IsNullOrEmpty(_drafts.GetValueOrDefault(nodeId)))
			_drafts[nodeId] = text;
	}

	[RelayCommand(CanExecute = nameof(IsRunning))]
	private async Task StopAsync()
	{
		var id = NodeId;
		if (id == null)
			return;

		try
		{
			await _client.TurnStopAsync(id);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "turn.stop failed for {NodeId}", id);
			Error = Constants.TurnTexts.StopFailed + ex.Message;
		}
	}

	// ---- notifications ----

	/// <summary>Reads a <c>turn.*</c> notification (on the client's thread) and applies it on the UI thread; false when it is not one.</summary>
	private bool TryHandleTurnNotification(RemoteNotification notification)
	{
		if (notification.TryGetTurnStarted(out var started))
			_ui.Post(() => ApplyStarted(started));
		else if (notification.TryGetTurnStatus(out var status))
			_ui.Post(() => ApplyStatus(status));
		else if (notification.TryGetTurnDelta(out var delta))
			_ui.Post(() => ApplyDelta(delta));
		else if (notification.TryGetTurnTool(out var tool))
			_ui.Post(() => ApplyTool(tool));
		else if (notification.TryGetTurnUsage(out var usage))
			_ui.Post(() => ApplyUsage(usage));
		else if (notification.TryGetTurnCompleted(out var completed))
			_ui.Post(() => ApplyCompleted(completed));
		else
			return false;

		return true;
	}

	private SessionTurnsState TurnsOf(string nodeId)
	{
		if (!_turnStates.TryGetValue(nodeId, out var state))
		{
			state = new SessionTurnsState();
			_turnStates[nodeId] = state;
		}

		return state;
	}

	private void ApplyStarted(TurnStartedPayload payload)
	{
		var state = TurnsOf(payload.NodeId);
		state.GetOrAdd(payload.TurnId);
		state.Error = null;
		AfterTurnChange(payload.NodeId);
	}

	private void ApplyStatus(TurnStatusPayload payload)
	{
		var turn = TurnsOf(payload.NodeId).GetOrAdd(payload.TurnId);
		turn.State = payload.State;
		turn.Detail = payload.Detail;
		if (payload.State != TurnState.Queued && turn.StartedAtUnixMs == null)
			turn.StartedAtUnixMs = Clock.GetUtcNow().ToUnixTimeMilliseconds();

		AfterTurnChange(payload.NodeId);
	}

	private void ApplyDelta(TurnDeltaPayload payload)
	{
		var turn = ActivateOnOutput(payload.NodeId, payload.TurnId);
		if (!turn.AddDelta(payload))
			return;   // a repeated delta (a replay) adds nothing

		AfterTurnChange(payload.NodeId);
	}

	private void ApplyTool(TurnToolPayload payload)
	{
		ActivateOnOutput(payload.NodeId, payload.TurnId).ApplyTool(payload);
		AfterTurnChange(payload.NodeId);
	}

	private void ApplyUsage(TurnUsagePayload payload)
	{
		var turn = TurnsOf(payload.NodeId).GetOrAdd(payload.TurnId);
		turn.Usage = $"{payload.InputTokens} in / {payload.OutputTokens} out" + (payload.CostUsd > 0 ? $", ${payload.CostUsd:0.0000}" : string.Empty);
		AfterTurnChange(payload.NodeId);
	}

	/// <summary>Output of a turn proves it runs, even when its status notification was missed.</summary>
	private TurnLiveState ActivateOnOutput(string nodeId, string turnId)
	{
		var turn = TurnsOf(nodeId).GetOrAdd(turnId);
		if (turn.State == TurnState.Queued)
		{
			turn.State = TurnState.Running;
			turn.StartedAtUnixMs ??= Clock.GetUtcNow().ToUnixTimeMilliseconds();
		}

		return turn;
	}

	private void ApplyCompleted(TurnCompletedPayload payload)
	{
		var state = TurnsOf(payload.NodeId);
		var turn = payload.TurnId == null ? state.Turns.Find(t => t.IsActive) ?? state.Turns.FirstOrDefault() : state.Turns.Find(t => t.TurnId == payload.TurnId);
		if (turn != null)
			state.Turns.Remove(turn);

		if (!string.IsNullOrWhiteSpace(payload.Error))
		{
			state.Error = payload.Error;
			if (payload.NodeId != NodeId)
				RememberErrorOfOtherSession(payload.NodeId);
		}

		if (state.IsEmpty)
			_turnStates.Remove(payload.NodeId);

		if (payload.NodeId != NodeId)
			return;

		_stateEvents++;
		// The daemon's own running flag follows in session.changed; until then the generic working row must not flash up.
		if (state.Turns.Count == 0)
			_daemonRunning = false;

		RecomputeRunning();
		_handoffTask = HandOffAsync(payload.NodeId);
	}

	/// <summary>
	/// The turn ended: the persisted entries are applied first and only then the live row is cleared, so the answer is never on screen twice
	/// (preview and entry) and there is no blank gap between them.
	/// </summary>
	private async Task HandOffAsync(string nodeId)
	{
		if (_current != null)
			await RequestAppend();

		if (nodeId == NodeId)
			PushLive();
	}

	/// <summary>Errors of sessions that are not open stay until their next send; only the newest few are kept.</summary>
	private void RememberErrorOfOtherSession(string nodeId)
	{
		_errorOrder.Enqueue(nodeId);
		while (_errorOrder.Count > Constants.TurnLimits.RememberedErrorSessions)
		{
			var oldest = _errorOrder.Dequeue();
			if (oldest != NodeId && _turnStates.TryGetValue(oldest, out var state) && state.Turns.Count == 0)
				_turnStates.Remove(oldest);
		}
	}

	private void ClearTurnError(string nodeId)
	{
		if (!_turnStates.TryGetValue(nodeId, out var state))
			return;

		state.Error = null;
		if (state.IsEmpty)
			_turnStates.Remove(nodeId);

		PushLive();
	}

	private void AfterTurnChange(string nodeId)
	{
		if (nodeId != NodeId)
			return;

		RecomputeRunning();
		ScheduleLiveFlush();
	}

	// ---- running flag and the live row ----

	/// <summary>The daemon's running flag of the open session changed (detail or <c>session.changed</c>).</summary>
	private void SetDaemonRunning(bool running)
	{
		if (running && !_daemonRunning)
			_genericSinceUnixMs = Clock.GetUtcNow().ToUnixTimeMilliseconds();

		_daemonRunning = running;
		RecomputeRunning();
		ScheduleLiveFlush();
	}

	private void RecomputeRunning()
	{
		var active = NodeId != null && _turnStates.TryGetValue(NodeId, out var state) && state.HasActive;
		IsRunning = _daemonRunning || active;
		StateText = IsRunning ? "Running" : "Idle";
	}

	private void ScheduleLiveFlush()
	{
		if (LiveFlushInterval <= TimeSpan.Zero)
		{
			PushLive();
			return;
		}

		if (_liveFlushScheduled)
			return;

		_liveFlushScheduled = true;
		_ = Task.Delay(LiveFlushInterval).ContinueWith(_ => _ui.Post(() =>
		{
			_liveFlushScheduled = false;
			PushLive();
		}), TaskScheduler.Default);
	}

	/// <summary>Sends the current live row of the open session to the page (also after every load, which clears it).</summary>
	private void PushLive()
	{
		var id = NodeId;
		if (id == null)
			return;

		_turnStates.TryGetValue(id, out var state);
		_surface.SetLive((state ?? new SessionTurnsState()).Build(_daemonRunning, _genericSinceUnixMs));
	}

	/// <summary>A new open session starts without a running flag of its own; its detail brings the real one.</summary>
	private void ResetRunningForOpen()
	{
		_daemonRunning = false;
		_genericSinceUnixMs = null;
		RecomputeRunning();
	}
}
