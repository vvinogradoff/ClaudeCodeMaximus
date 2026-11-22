using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The toggles of the command bar: Streaming Session Mode (FR.19.1; turning it off while a process is alive asks first, FR.19.7) and the session
/// completion sound (FR.22.2). The mode comes with the session detail, whether a process is alive with the session state pushes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class CommandTogglesViewModel : ObservableObject
{
	private readonly ITessynClient _client;
	private readonly ILogger _logger;
	private Func<IDialogService?> _dialogs = () => null;
	private string? _nodeId;
	private int _token;
	private int _streamSeq;

	[ObservableProperty]
	private bool _isStreamingMode;

	[ObservableProperty]
	private bool _isStreamingAlive;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ToggleStreamingCommand))]
	private bool _isStreamingKnown;

	/// <summary>False for a Codex session: Streaming Session Mode does not apply to it, so the toggle is hidden, not faked (FR.33.1, FR.33.6).</summary>
	[ObservableProperty]
	private bool _isStreamingApplicable = true;

	public CommandTogglesViewModel(ITessynClient client, SessionOptionsViewModel options, ILogger logger)
	{
		_client = client;
		Options = options;
		_logger = logger;
		options.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName is nameof(SessionOptionsViewModel.PlayCompletionSound) or nameof(SessionOptionsViewModel.IsKnown))
			{
				OnPropertyChanged(nameof(PlayCompletionSound));
				OnPropertyChanged(nameof(SoundChecked));
				OnPropertyChanged(nameof(IsSoundKnown));
				ToggleSoundCommand.NotifyCanExecuteChanged();
			}
		};
	}

	public SessionOptionsViewModel Options { get; }

	public bool PlayCompletionSound => Options.PlayCompletionSound;

	// The two-way face of the toggle buttons (UI Automation's Toggle does not run a button's Command).

	public bool StreamingChecked
	{
		get => IsStreamingMode;
		set
		{
			if (value != IsStreamingMode && IsStreamingKnown)
				_ = ToggleStreamingAsync();
		}
	}

	public bool SoundChecked
	{
		get => Options.PlayCompletionSound;
		set
		{
			if (value != Options.PlayCompletionSound && Options.IsKnown)
				_ = ToggleSoundAsync();
		}
	}

	partial void OnIsStreamingModeChanged(bool value) => OnPropertyChanged(nameof(StreamingChecked));

	public bool IsSoundKnown => Options.IsKnown;

	/// <summary>Completes when the last streaming change has been answered (tests).</summary>
	public Task Idle { get; private set; } = Task.CompletedTask;

	public void Connect(Func<IDialogService?> dialogs) => _dialogs = dialogs;

	/// <summary>A session opened (or none): nothing of the previous one shows until its detail arrives.</summary>
	public void Open(string? nodeId)
	{
		_token++;
		_nodeId = nodeId;
		IsStreamingKnown = false;
		IsStreamingMode = false;
		IsStreamingAlive = false;
	}

	/// <summary>The session detail arrived.</summary>
	public void ApplyDetail(string nodeId, bool streamingMode, bool streamingAlive)
	{
		if (nodeId != _nodeId)
			return;

		IsStreamingMode = streamingMode;
		IsStreamingAlive = streamingAlive;
		IsStreamingKnown = true;
	}

	/// <summary>A state push of the open session: a process may have started or ended.</summary>
	public void ApplyAlive(string nodeId, bool streamingAlive)
	{
		if (nodeId == _nodeId)
			IsStreamingAlive = streamingAlive;
	}

	private bool CanToggleStreaming() => IsStreamingKnown;

	[RelayCommand(CanExecute = nameof(CanToggleStreaming))]
	private Task ToggleStreamingAsync() => Idle = ChangeStreamingAsync(!IsStreamingMode);

	private async Task ChangeStreamingAsync(bool enable)
	{
		var id = _nodeId;
		if (id == null)
			return;

		var token = _token;
		var seq = ++_streamSeq;
		var before = IsStreamingMode;
		IsStreamingMode = enable;   // shows at once; the answer or a failure sets it right
		try
		{
			var result = await _client.SessionSetStreamingAsync(id, enable);
			if (!enable && result.NeedsConfirm)
			{
				var dialogs = _dialogs();
				var confirmed = dialogs != null && await dialogs.ConfirmAsync(new ConfirmDialogRequest(   // no dialog service counts as not confirmed
					Constants.ToolbarTexts.StreamingOffTitle, Constants.ToolbarTexts.StreamingOffMessage, Constants.ToolbarTexts.StreamingOffConfirm, IsDestructive: true));
				if (!confirmed)
				{
					if (token == _token && seq == _streamSeq)
						IsStreamingMode = true;

					return;
				}

				result = await _client.SessionSetStreamingAsync(id, false, force: true);
			}

			if (token == _token && seq == _streamSeq)   // a newer change has been made since: this answer is stale
				IsStreamingMode = result.Enabled;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "session.setStreaming failed for {NodeId}", id);
			if (token == _token && seq == _streamSeq)
			{
				IsStreamingMode = before;
				Options.Notice = Constants.ToolbarTexts.StreamingFailed + ex.Message;
			}
		}
	}

	private bool CanToggleSound() => Options.IsKnown;

	[RelayCommand(CanExecute = nameof(CanToggleSound))]
	private Task ToggleSoundAsync() => Options.SetAsync(playCompletionSound: !Options.PlayCompletionSound);
}
