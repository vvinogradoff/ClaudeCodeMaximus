using System;
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

		try
		{
			var host = _pool.GetHost(_profiles.Select(node, settings: null));
			CancelIdleUnload(threadId);
			if (!host.IsThreadLoaded(threadId))
			{
				var config = await _config.BuildAsync(node, cancellationToken).ConfigureAwait(false);
				try
				{
					await ResumeAsync(host, threadId, config, cancellationToken).ConfigureAwait(false);
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

			await host.CompactThreadAsync(threadId, cancellationToken).ConfigureAwait(false);

			TurnFinish finish;
			try
			{
				finish = await listener.Finished.WaitAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				if (listener.TurnId is { } turnId)
					_ = InterruptQuietlyAsync(host, threadId, turnId);
				throw;
			}

			ScheduleIdleUnload(host, threadId);
			if (finish.Crash is { } crash)
				return CompactionFailed(HostStoppedText(crash));

			var completion = finish.Completion!;
			if (completion.Outcome == CodexTurnOutcome.Completed)
			{
				_log.Information("Codex compacted thread {ThreadId} of session {NodeId}", threadId, node.NodeId);
				return new SessionCompactionResultModel(SessionCompactionOutcome.Compacted, null, Constants.Codex.Texts.Compacted, SessionDetached: false);
			}

			return CompactionFailed(completion.Error?.SystemText ?? Constants.Codex.Texts.InterruptedElsewhere);
		}
		catch (CodexNotFoundException ex)
		{
			return CompactionFailed(ex.Message);
		}
		catch (CodexHostCrashedException ex)
		{
			return CompactionFailed(HostStoppedText(ex.Crash));
		}
		catch (Exception ex) when (ex is CodexRpcException or TimeoutException or CodexConnectionLostException && !cancellationToken.IsCancellationRequested)
		{
			_log.Warning(ex, "Codex compaction of session {NodeId} failed", node.NodeId);
			return CompactionFailed(ex.Message);
		}
	}

	private SessionCompactionResultModel CompactionFailed(string error)
	{
		_log.Warning("Codex compaction failed: {Error}", error);
		return new SessionCompactionResultModel(SessionCompactionOutcome.Failed, error, string.Format(Constants.Compaction.FailedMessageFormat, error), false);
	}
}
