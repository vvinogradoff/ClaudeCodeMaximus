using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>The one-shot flags of a send: what the user had switched on (New Branch, Auto-Compact) when the send was taken.</summary>
/// <remarks>Created by Claude</remarks>
public readonly record struct OneShotFlags(bool NewBranch, bool AutoCompact)
{
	public bool Any => NewBranch || AutoCompact;
}

/// <summary>
/// The instruction toolbar above the prompt box (FR.11.1): Auto-Commit (tri-state), New Branch and Auto-Compact (one-shot, they travel with the next
/// <c>turn.send</c> and switch off), Auto-Document (sticky), Compact now and Terminate (detach the Claude session, FR.11.7). The sticky options are
/// the daemon's (<see cref="SessionOptionsViewModel"/>); the one-shot flags are the client's, kept per session while the pane lives.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class InstructionToolbarViewModel : ObservableObject
{
	private readonly ITessynClient _client;
	private readonly ILogger _logger;
	private readonly Dictionary<string, OneShotFlags> _flags = new();
	private Func<IDialogService?> _dialogs = () => null;
	private Func<(string? Model, string? Effort, string? Profile)> _picks = () => (null, null, null);

	[ObservableProperty]
	private bool _hasSession;

	[ObservableProperty]
	private bool _isRunning;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(CompactCommand), nameof(ClearCommand))]
	private bool _isBusy;

	[ObservableProperty]
	private bool _newBranch;

	[ObservableProperty]
	private bool _autoCompact;

	public InstructionToolbarViewModel(ITessynClient client, SessionOptionsViewModel options, ILogger logger)
	{
		_client = client;
		Options = options;
		_logger = logger;
		options.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName is nameof(SessionOptionsViewModel.AutoCommit) or nameof(SessionOptionsViewModel.IsKnown) or nameof(SessionOptionsViewModel.Notice))
				Refresh();
		};
	}

	public SessionOptionsViewModel Options { get; }

	public bool IsToolbarEnabled => HasSession && Options.IsKnown;

	public string AutoCommitLabel => Options.AutoCommit switch
	{
		AutoCommitSetting.Commit => Constants.ToolbarTexts.AutoCommitCommit,
		AutoCommitSetting.DoNotCommit => Constants.ToolbarTexts.AutoCommitDoNot,
		_ => Constants.ToolbarTexts.AutoCommitNoPreference,
	};

	public string AutoCommitTip => Options.AutoCommit switch
	{
		AutoCommitSetting.Commit => Constants.ToolbarTexts.AutoCommitCommitTip,
		AutoCommitSetting.DoNotCommit => Constants.ToolbarTexts.AutoCommitDoNotTip,
		_ => Constants.ToolbarTexts.AutoCommitNoPreferenceTip,
	};

	public bool AutoCommitIsCommit => Options.AutoCommit == AutoCommitSetting.Commit;

	public bool AutoCommitIsDoNot => Options.AutoCommit == AutoCommitSetting.DoNotCommit;

	public string Notice => Options.Notice;

	/// <summary>Where Compact and Terminate find the dialogs and the model, effort and profile the user picked.</summary>
	public void Connect(Func<IDialogService?> dialogs, Func<(string? Model, string? Effort, string? Profile)> picks)
	{
		_dialogs = dialogs;
		_picks = picks;
	}

	/// <summary>A session opened (or none): its own one-shot flags show.</summary>
	public void Open(string? nodeId)
	{
		HasSession = nodeId != null;
		var flags = nodeId != null && _flags.TryGetValue(nodeId, out var f) ? f : default;
		NewBranch = flags.NewBranch;
		AutoCompact = flags.AutoCompact;
		Refresh();
	}

	private void Refresh()
	{
		OnPropertyChanged(string.Empty);
		CycleAutoCommitCommand.NotifyCanExecuteChanged();
		ToggleAutoDocumentCommand.NotifyCanExecuteChanged();
		ToggleNewBranchCommand.NotifyCanExecuteChanged();
		ToggleAutoCompactCommand.NotifyCanExecuteChanged();
		CompactCommand.NotifyCanExecuteChanged();
		ClearCommand.NotifyCanExecuteChanged();
	}

	private void Remember()
	{
		if (Options.NodeId is { } id)
			_flags[id] = new OneShotFlags(NewBranch, AutoCompact);
	}

	partial void OnNewBranchChanged(bool value)
	{
		Remember();
		OnPropertyChanged(nameof(NewBranchChecked));
	}

	partial void OnAutoCompactChanged(bool value)
	{
		Remember();
		OnPropertyChanged(nameof(AutoCompactChecked));
	}

	// The two-way face of the toggle buttons: a click (and UI Automation's Toggle, which does not run a button's Command) sets these, and they do what the commands do.

	public bool NewBranchChecked
	{
		get => NewBranch;
		set
		{
			if (value != NewBranch && CanChange())
				ToggleNewBranch();
		}
	}

	public bool AutoCompactChecked
	{
		get => AutoCompact;
		set
		{
			if (value != AutoCompact && CanChange())
				ToggleAutoCompact();
		}
	}

	public bool AutoDocumentChecked
	{
		get => Options.AutoDocument;
		set
		{
			if (value != Options.AutoDocument && CanChange())
				_ = ToggleAutoDocumentAsync();
		}
	}

	/// <summary>
	/// The one-shot flags go out with the send being made now and switch off (FR.11.4, FR.11.6); <see cref="Restore"/> gives them back when the
	/// send did not happen.
	/// </summary>
	public OneShotFlags TakeOneShots()
	{
		var flags = new OneShotFlags(NewBranch, AutoCompact);
		NewBranch = false;
		AutoCompact = false;
		return flags;
	}

	/// <summary>A send that did not happen switches its flags on again, in the session it was made in.</summary>
	public void Restore(string nodeId, OneShotFlags flags)
	{
		if (!flags.Any)
			return;

		if (nodeId == Options.NodeId)
		{
			NewBranch |= flags.NewBranch;
			AutoCompact |= flags.AutoCompact;
		}
		else
		{
			var old = _flags.GetValueOrDefault(nodeId);
			_flags[nodeId] = new OneShotFlags(old.NewBranch | flags.NewBranch, old.AutoCompact | flags.AutoCompact);
		}
	}

	private bool CanChange() => IsToolbarEnabled;

	/// <summary>Do-not-commit, Commit, No preference, Do-not-commit (FR.11.3).</summary>
	[RelayCommand(CanExecute = nameof(CanChange))]
	private Task CycleAutoCommitAsync() => Options.SetAsync(autoCommit: Options.AutoCommit switch
	{
		AutoCommitSetting.DoNotCommit => AutoCommitSetting.Commit,
		AutoCommitSetting.Commit => AutoCommitSetting.NoPreference,
		_ => AutoCommitSetting.DoNotCommit,
	});

	[RelayCommand(CanExecute = nameof(CanChange))]
	private Task ToggleAutoDocumentAsync() => Options.SetAsync(autoDocument: !Options.AutoDocument);

	/// <summary>The daemon knows nothing of the one-shot flags until the send, so a mid-run change only says when it applies (FR.11.12).</summary>
	[RelayCommand(CanExecute = nameof(CanChange))]
	private void ToggleNewBranch()
	{
		NewBranch = !NewBranch;
		if (IsRunning)
			Options.Notice = string.Format(Constants.ToolbarTexts.NewBranchNotice, NewBranch ? Constants.ToolbarTexts.On : Constants.ToolbarTexts.Off);
	}

	[RelayCommand(CanExecute = nameof(CanChange))]
	private void ToggleAutoCompact()
	{
		AutoCompact = !AutoCompact;
		if (IsRunning)
			Options.Notice = string.Format(Constants.ToolbarTexts.AutoCompactNotice, AutoCompact ? Constants.ToolbarTexts.On : Constants.ToolbarTexts.Off);
	}

	private bool CanAct() => IsToolbarEnabled && !IsBusy;

	/// <summary>Compacts the session now, with no turn (<c>session.compact</c>).</summary>
	[RelayCommand(CanExecute = nameof(CanAct))]
	private async Task CompactAsync()
	{
		var id = Options.NodeId;
		if (id == null)
			return;

		IsBusy = true;
		try
		{
			var (model, effort, profile) = _picks();
			await _client.SessionCompactAsync(id, model, effort, profile);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "session.compact failed for {NodeId}", id);
			Options.Notice = Constants.ToolbarTexts.CompactFailed + ex.Message;
		}
		finally
		{
			IsBusy = false;
		}
	}

	/// <summary>Terminate (FR.11.7): after a confirmation the Claude session is detached; nothing is sent.</summary>
	[RelayCommand(CanExecute = nameof(CanAct))]
	private async Task ClearAsync()
	{
		var id = Options.NodeId;
		if (id == null)
			return;

		var dialogs = _dialogs();
		if (dialogs != null && !await dialogs.ConfirmAsync(new ConfirmDialogRequest(Constants.ToolbarTexts.ClearTitle, Constants.ToolbarTexts.ClearMessage, Constants.ToolbarTexts.ClearConfirm, IsDestructive: true)))
			return;

		IsBusy = true;
		try
		{
			var result = await _client.SessionResetConversationAsync(id);
			if (!result.Cleared)
				Options.Notice = Constants.ToolbarTexts.ClearRefused;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "session.resetConversation failed for {NodeId}", id);
			Options.Notice = Constants.ToolbarTexts.ClearFailed + ex.Message;
		}
		finally
		{
			IsBusy = false;
		}
	}
}
