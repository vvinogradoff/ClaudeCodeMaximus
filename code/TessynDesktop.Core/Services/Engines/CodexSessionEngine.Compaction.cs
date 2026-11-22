using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Codex.Events;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

public sealed partial class CodexSessionEngine
{
	/// <summary>
	/// Auto-Compact and Compact now on a Codex session (FR.33.12, decision D7): <c>thread/compact/start</c>, which Codex runs as a turn of its own
	/// with a <c>contextCompaction</c> item; that item writes the COMPACTION separator. The thread id is kept and no Claude process starts. The
	/// caller holds the session's turn lock.
	/// </summary>
	public async Task<SessionCompactionResultModel> CompactConversationAsync(SessionNodeModel node, CancellationToken cancellationToken)
	{
		var threadId = node.CodexThreadId;
		if (string.IsNullOrEmpty(threadId))
			return new SessionCompactionResultModel(SessionCompactionOutcome.NothingToCompact, null, Constants.Compaction.NothingToCompactMessage, false);

		// The compaction is the node's running work while it lasts, so Stop and InterruptAsync reach it as they reach a turn.
		using var compactCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var profile = _profiles.Select(node, settings: null);
		var running = new RunningTurn(compactCancel, profile.ProfileId) { ThreadId = threadId };
		_running[node.NodeId] = running;
		var ct = compactCancel.Token;
		JsonObject? config = null;
		CodexAppServerHost? usedHost = null;

		try
		{
			var host = _pool.GetHost(profile);
			usedHost = host;
			CancelIdleUnload(threadId);
			if (!host.IsThreadLoaded(threadId))
			{
				config = await _config.BuildAsync(node, ct).ConfigureAwait(false);
				try
				{
					await ResumeAsync(host, threadId, config, ct).ConfigureAwait(false);
				}
				catch (CodexThreadException ex) when (ex.Kind == CodexThreadErrorKind.ThreadGone)
				{
					DetachConversation(node);
					return CompactionFailed(Constants.Codex.Texts.CompactThreadGone);
				}
				catch (CodexThreadException ex) when (ex.Kind == CodexThreadErrorKind.ActiveWriter)
				{
					return CompactionFailed(Constants.Codex.Texts.ActiveWriter);
				}
			}

			using var listener = new TurnListener(host, threadId, e =>
			{
				if (e is CodexCompactionEvent)
					_files.AppendCompactionSeparator(node.FileName);
			});

			// Not cancelled half-way, like turn/start: once Codex accepted it, a Stop knows there is a compaction turn to interrupt.
			await host.CompactThreadAsync(threadId).ConfigureAwait(false);

			TurnFinish finish;
			try
			{
				finish = await listener.Finished.WaitAsync(ct).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				await InterruptCompactionAsync(host, node, threadId, listener).ConfigureAwait(false);
				throw;
			}

			if (finish.Crash is { } crash)
				return CompactionFailed(HostStoppedText(crash));
			if (finish.Stopped is { } reason)
				return CompactionFailed(StoppedText(reason));

			var completion = finish.Completion!;
			if (completion.Outcome == CodexTurnOutcome.Completed)
			{
				_log.Information("Codex compacted thread {ThreadId} of session {NodeId}", threadId, node.NodeId);
				return new SessionCompactionResultModel(SessionCompactionOutcome.Compacted, null, Constants.Codex.Texts.Compacted, SessionDetached: false);
			}

			return CompactionFailed(completion.Error?.SystemText ?? Constants.Codex.Texts.InterruptedElsewhere);
		}
		catch (Exception ex) when (ex is CodexNotFoundException or InvalidOperationException)
		{
			return CompactionFailed(ex.Message);
		}
		catch (CodexHostCrashedException ex)
		{
			return CompactionFailed(HostStoppedText(ex.Crash));
		}
		catch (Exception ex) when (ex is CodexRpcException or TimeoutException or CodexConnectionLostException && !ct.IsCancellationRequested)
		{
			var reason = _config.RedactText(ex.Message, config, node.AgentToken);
			_log.Warning("Codex compaction of session {NodeId} failed ({Type}): {Error}", node.NodeId, ex.GetType().Name, reason);
			return CompactionFailed(reason);
		}
		catch (Exception ex) when (ct.IsCancellationRequested && ex is not OperationCanceledException)
		{
			throw new OperationCanceledException(ct);
		}
		finally
		{
			_running.TryRemove(new KeyValuePair<string, RunningTurn>(node.NodeId, running));
			if (usedHost is not null)
				ScheduleIdleUnload(usedHost, threadId);
		}
	}

	/// <summary>Stop during a compaction: <c>turn/interrupt</c> on its turn, and up to 15 s for Codex to confirm it (FR.33.8); a Warning without it.</summary>
	private async Task InterruptCompactionAsync(CodexAppServerHost host, SessionNodeModel node, string threadId, TurnListener listener)
	{
		var confirm = Task.Delay(Constants.Codex.StopConfirmTimeout, _time);
		var turnId  = listener.TurnId;
		if (turnId is null)
		{
			// The compaction turn has not reported its id yet: wait for turn/started (or the end) within the same 15 s.
			while (listener.TurnId is null && !listener.Finished.IsCompleted && !confirm.IsCompleted)
				await Task.WhenAny(listener.Finished, confirm, Task.Delay(TimeSpan.FromMilliseconds(20))).ConfigureAwait(false);
			turnId = listener.TurnId;
		}

		if (turnId is not null && !listener.Finished.IsCompleted)
			_ = InterruptQuietlyAsync(host, threadId, turnId);

		if (await Task.WhenAny(listener.Finished, confirm).ConfigureAwait(false) != listener.Finished)
			_log.Warning("Codex did not confirm the interrupt of the compaction of session {NodeId} within {Timeout}", node.NodeId, Constants.Codex.StopConfirmTimeout);
	}

	private SessionCompactionResultModel CompactionFailed(string error)
	{
		_log.Warning("Codex compaction failed: {Error}", error);
		return new SessionCompactionResultModel(SessionCompactionOutcome.Failed, error, string.Format(Constants.Compaction.FailedMessageFormat, error), false);
	}
}
