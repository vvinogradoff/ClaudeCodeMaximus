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

		public async Task AfterResponseWhileLockedAsync()
		{
			// Auto-Compact (FR.11.6): _midRunAutoCompactState reflects any mid-run toggle change. Runs inside the send flow, which
			// still holds the node's turn lock until the engine releases it.
			if (!_owner._midRunAutoCompactState)
				return;

			await _owner.SendCompactionPromptAsync(turnLockHeld: true);
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
