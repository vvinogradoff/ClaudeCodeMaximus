using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReactiveUI;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.ViewModels;

public sealed partial class TurnController
{
	/// <summary>
	/// Renders one engine turn into the session's message list (the UI half of the former <c>HandleStreamEvent</c> and
	/// <c>SendAsyncCore</c>). The user entry and the interruption line arrive on the caller's thread and are added in place, as the
	/// controller always did; everything that comes from the claude stream is posted to the UI thread, in order.
	/// </summary>
	/// <remarks>Created by Claude</remarks>
	private sealed class RunObserver : ITurnObserver
	{
		private readonly TurnController _owner;
		private readonly string _message;
		private MessageEntryViewModel? _compactionRow;

		public RunObserver(TurnController owner, string message)
		{
			_owner   = owner;
			_message = message;
		}

		private SessionContentViewModel Content => _owner._content;

		public void UserEntryAppended(DateTimeOffset timestamp, string? profileName)
		{
			Content.Messages.Add(new MessageEntryViewModel
			{
				Role        = Constants.SessionFile.RoleUser,
				Content     = _message,
				Timestamp   = timestamp,
				ProfileName = profileName,
				Source      = EntrySources.Resolve(Constants.SessionFile.RoleUser, null, _message),
			});
			_owner._node.LastPromptTime = timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
			_owner._node.LastPromptTimestamp = timestamp;
			_owner._node.ProfileName = profileName;

			Content.Messages.Add(new MessageEntryViewModel
			{
				Role       = Constants.SessionFile.RoleSystem,
				Content    = "Claude is thinking...",
				Timestamp  = DateTimeOffset.UtcNow,
				IsProgress = true,
			});
		}

		public void AssistantEntryAppended(string text, DateTimeOffset timestamp, string? modelId, string? effort) =>
			Dispatcher.UIThread.Post(() =>
			{
				Content.Messages.Add(new MessageEntryViewModel
				{
					Role       = Constants.SessionFile.RoleAssistant,
					Content    = text,
					Timestamp  = timestamp,
					ModelLabel = modelId == null ? null : SessionPromptComposer.BuildModelLabel(modelId, effort),
				});
				_owner._node.LastResponseTimestamp = timestamp;
			});

		public void CompactionAppended(DateTimeOffset timestamp) =>
			Dispatcher.UIThread.Post(() => Content.Messages.Add(new MessageEntryViewModel
			{
				Role      = Constants.SessionFile.RoleCompaction,
				Content   = string.Empty,
				Timestamp = timestamp,
			}));

		public void ProgressText(string text, DateTimeOffset timestamp) =>
			Dispatcher.UIThread.Post(() =>
			{
				var last = Content.Messages.Count > 0 ? Content.Messages[^1] : null;
				if (last?.Role == Constants.SessionFile.RoleSystem && last.IsProgress)
					last.Content = text;
				else
					Content.Messages.Add(new MessageEntryViewModel
					{
						Role       = Constants.SessionFile.RoleSystem,
						Content    = text,
						Timestamp  = timestamp,
						IsProgress = true,
					});
			});

		public void SystemError(string text, DateTimeOffset timestamp) =>
			Dispatcher.UIThread.Post(() => AddSystemRow(text, timestamp));

		public void ResultError(string text, DateTimeOffset timestamp) =>
			Dispatcher.UIThread.Post(() =>
			{
				_owner.RemoveProgressRows();
				AddSystemRow(text, timestamp);
			});

		public void ResultEnded() =>
			Dispatcher.UIThread.Post(_owner.RemoveProgressRows);

		public void SessionIdCaptured() =>
			Dispatcher.UIThread.Post(() =>
			{
				_owner.RaisePropertyChanged(nameof(CanClear));
				Content.NotifyClaudeSessionChanged();
			});

		public void AuthErrorChanged(bool hasAuthError) =>
			Dispatcher.UIThread.Post(() => _owner._profiles.HasAuthError = hasAuthError);

		public void StreamingProcessStarted() =>
			Dispatcher.UIThread.Post(_owner.NotifyStreamingStateChanged);

		public void ContextRetrying() =>
			Dispatcher.UIThread.Post(() =>
			{
				var last = Content.Messages.Count > 0 ? Content.Messages[^1] : null;
				if (last?.Role == Constants.SessionFile.RoleSystem && last.IsProgress)
					last.Content = Constants.Turns.ResumingWithHistoryText;
				else
					Content.Messages.Add(new MessageEntryViewModel
					{
						Role       = Constants.SessionFile.RoleSystem,
						Content    = Constants.Turns.ResumingWithHistoryText,
						Timestamp  = DateTimeOffset.UtcNow,
						IsProgress = true,
					});
			});

		public void Interrupted(DateTimeOffset timestamp)
		{
			_owner._anyCancelledInBatch = true;
			Content.Messages.Add(new MessageEntryViewModel
			{
				Role      = Constants.SessionFile.RoleSystem,
				Content   = Constants.Turns.InterruptedByUserText,
				Timestamp = timestamp,
			});
		}

		public async Task BeforeIdleAsync()
		{
			// Update the JSONL entry count before IsBusy goes false, so the watcher doesn't re-detect entries processed during this send
			// cycle. The parse runs off the UI thread; IsBusy stays true (watcher suppressed) while it is awaited. The engine may call this
			// on behalf of a turn this controller did not run (a remote one): then there is nothing to do.
			if (_owner.IsBusy)
				await Content.RefreshJsonlEntryCountAsync();
		}

		public void Idle()
		{
			if (!_owner.IsBusy)
				return;

			var t = _owner._thinkingTimer;
			_owner._thinkingTimer = null;
			t?.Stop();
			_owner.ThinkingDuration = string.Empty;
			_owner.IsBusy = false;
			_owner._node.IsRunning = false;

			// FR.22.1: only on natural completion of the whole batch, never after user Stop.
			if (!_owner._anyCancelledInBatch && _owner._node.Model.PlayCompletionSound)
				_owner._soundService.PlayCompletionSound(_owner._node.Model.NodeId);
		}

		// Auto-Compact (FR.11.6) is the engine's now; the toggle can still change while the turn runs, so the live state decides.
		public bool ShouldAutoCompact(bool requested) => _owner._midRunAutoCompactState;

		public void CompactionStarted()
		{
			_compactionRow = new MessageEntryViewModel
			{
				Role       = Constants.SessionFile.RoleSystem,
				Content    = "Compacting session...",
				Timestamp  = DateTimeOffset.UtcNow,
				IsProgress = true,
			};
			var row = _compactionRow;
			Dispatcher.UIThread.Post(() => Content.Messages.Add(row));
		}

		/// <summary>The UI part of a compaction: the reload of Messages, the outcome lines and the toggle reset (the engine did the rewrite and the auth error).</summary>
		public void CompactionFinished(SessionCompactionResultModel result)
		{
			var progressRow = _compactionRow;
			switch (result.Outcome)
			{
				case SessionCompactionOutcome.NothingToCompact:
					// Only this run's own row: nothing was sent, so other progress rows (a queued send) stay.
					Dispatcher.UIThread.Post(() =>
					{
						if (progressRow != null) Content.Messages.Remove(progressRow);
						Content.AddSystemMessage(result.SystemMessage);
					});
					break;

				case SessionCompactionOutcome.Compacted:
					Dispatcher.UIThread.Post(() =>
					{
						Content.Messages.Clear();
						var entries = _owner._fileService.ReadEntries(_owner._node.FileName);
						foreach (var entry in entries)
						{
							if (entry.Role != Constants.SessionFile.RoleCompaction
							    && string.IsNullOrWhiteSpace(entry.Content))
								continue;
							Content.Messages.Add(SessionContentViewModel.EntryToViewModel(entry));
						}

						// The service detached the JSONL (FR.11.8); only when it had a live session is there anything to announce.
						if (result.SessionDetached)
						{
							Content.AddSystemMessage(result.SystemMessage);
							_owner.RaisePropertyChanged(nameof(CanClear));
							Content.NotifyClaudeSessionChanged();
						}
					});
					break;

				default:
					// The auth error itself came through AuthErrorChanged.
					Dispatcher.UIThread.Post(() =>
					{
						_owner.RemoveProgressRows();
						Content.AddSystemMessage(result.SystemMessage);
					});
					break;
			}

			_owner.IsAutoCompact = false;
		}

		private void AddSystemRow(string text, DateTimeOffset timestamp) =>
			Content.Messages.Add(new MessageEntryViewModel
			{
				Role      = Constants.SessionFile.RoleSystem,
				Content   = text,
				Timestamp = timestamp,
			});
	}
}
