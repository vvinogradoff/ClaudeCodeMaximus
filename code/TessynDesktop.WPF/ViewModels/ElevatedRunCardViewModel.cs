using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Switch;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The state and the actions of one elevated-run card (FR.23.32, FR.15.19, FR.32.16): the facts of the request, the buttons that match its status, and the part of the launch that
/// belongs to this window: after <c>elevated.approve</c> it starts exactly the returned description through the <see cref="IElevatedRunLauncher"/> and reports <c>elevated.launched</c> or
/// <c>elevated.launchFailed</c>. The status itself is the daemon's: the card follows <c>attention.list</c> (through <see cref="ApplyRun"/>) and <c>elevated.changed</c> (through <see cref="RefreshAsync"/>).
/// </summary>
/// <remarks>
/// Only one click starts a run: <see cref="ApproveCommand"/> is unavailable from the moment it runs, and the daemon accepts the first approve only (a second one answers "no longer pending").
/// Nothing here approves by itself; there is no default button and no key that triggers it (the view sets none).
/// </remarks>
/// <remarks>Created by Claude</remarks>
public sealed partial class ElevatedRunCardViewModel : ObservableObject, IDisposable
{
	private readonly string _itemNodeId;
	private readonly ITessynClient _client;
	private readonly IElevatedRunLauncher? _launcher;
	private readonly IClipboardWriter? _clipboard;
	private readonly IUiDispatcher _ui;
	private readonly ILogger _logger;
	private readonly TimeProvider _time;
	private readonly Action _onFinished;
	private readonly CancellationTokenSource _cts = new();
	private ITimer? _timer;
	private int _ticks;
	private bool _approving;
	private bool _declining;
	private bool _disposed;

	[ObservableProperty]
	private ElevatedRunDto _run;

	/// <summary>The text under the card's buttons when something went wrong (a refused approve, a failed call).</summary>
	[ObservableProperty]
	private string? _error;

	/// <summary>The optional note of Decline (the daemon puts it into the outcome the agent gets).</summary>
	[ObservableProperty]
	private string _noteText = string.Empty;

	/// <summary>True while this window waits for the Windows administrator prompt: the launcher has not answered yet.</summary>
	[ObservableProperty]
	private bool _isPromptOpen;

	/// <summary>The last 20 lines of the output while the Output expander is open on a running card.</summary>
	[ObservableProperty]
	private string _outputText = string.Empty;

	[ObservableProperty]
	private bool _isOutputExpanded;

	public ElevatedRunCardViewModel(
		ElevatedRunDto run, string itemNodeId, ITessynClient client, IElevatedRunLauncher? launcher, IClipboardWriter? clipboard,
		IUiDispatcher ui, ILogger logger, TimeProvider time, Action onFinished)
	{
		_run = run;
		_itemNodeId = itemNodeId;
		_client = client;
		_launcher = launcher;
		_clipboard = clipboard;
		_ui = ui;
		_logger = logger;
		_time = time;
		_onFinished = onFinished;
		ManageTimer();
	}

	public string Id => Run.Id;

	public string Reason => Run.Reason;

	/// <summary>The working directory: the full path on this PC; the folder name when the daemon sent only that.</summary>
	public string WorkingDirectory => Run.WorkingDir ?? Run.WorkingDirName ?? string.Empty;

	/// <summary>The command, verbatim (FR.23.32): never cut, never changed.</summary>
	public string Command => Run.Command;

	public string? LogPath => Run.LogPath;

	public bool HasLogPath => !string.IsNullOrEmpty(Run.LogPath);

	public string LogPathText => HasLogPath ? string.Format(Constants.ElevatedRuns.LogPathFormat, Run.LogPath) : string.Empty;

	/// <summary>True on the supervisor's card of a worker's request: the requester is not the session whose Focus this is.</summary>
	public bool IsWorkerRequest => !string.Equals(Run.Requester.NodeId, _itemNodeId, StringComparison.OrdinalIgnoreCase);

	public string RequestedByText => string.Format(
		IsWorkerRequest ? Constants.ElevatedRuns.RequestedByWorkerFormat : Constants.ElevatedRuns.RequestedByFormat, Run.Requester.Name, Run.Requester.ShortId);

	public string TimeLimitText => string.Format(Constants.ElevatedRuns.TimeLimitFormat, Run.TimeoutMinutes);

	public bool IsPending => Run.Status == ElevatedRunStatus.Pending;

	public bool IsLaunching => Run.Status == ElevatedRunStatus.Launching;

	public bool IsRunning => Run.Status == ElevatedRunStatus.Running;

	public bool IsFinal => ElevatedRunText.IsFinal(Run.Status);

	/// <summary>The line under the facts of a pending card: <c>Expires 14:35</c> (local time).</summary>
	public string ExpiresText => IsPending
		? string.Format(Constants.ElevatedRuns.ExpiresFormat, Run.ExpiresUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture))
		: string.Empty;

	/// <summary>Replaces the buttons while the run launches: the prompt is open in this window, or the start is under way (here or in another client).</summary>
	public string LaunchingText => IsPromptOpen ? Constants.ElevatedRuns.WaitingForPromptText : Constants.ElevatedRuns.StartingText;

	public string RunningText
	{
		get
		{
			var started = Run.StartedUtc ?? Run.CreatedUtc;
			return string.Format(
				Constants.ElevatedRuns.RunningFormat, started.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture),
				ElevatedRunText.FormatDuration(_time.GetUtcNow() - started), Run.TimeoutMinutes);
		}
	}

	/// <summary>The outcome line of a finished run (<c>Completed: exit code 0, 3m 12s</c>, ...).</summary>
	public string OutcomeText => ElevatedRunText.OutcomeLine(Run);

	public bool HasError => !string.IsNullOrEmpty(Error);

	/// <summary>Whether a launcher exists in this window (a client without one shows the buttons disabled).</summary>
	public bool CanLaunch => _launcher != null;

	private bool CanApprove() => IsPending && !_approving && !_declining;

	private bool CanDecline() => IsPending && !_approving && !_declining;

	private bool CanStop() => IsRunning;

	partial void OnRunChanged(ElevatedRunDto value) => RaiseRunChanged();

	partial void OnIsPromptOpenChanged(bool value) => OnPropertyChanged(nameof(LaunchingText));

	partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));

	partial void OnIsOutputExpandedChanged(bool value)
	{
		if (value && !_disposed)
			_ = RefreshAsync();
	}

	private void RaiseRunChanged()
	{
		OnPropertyChanged(string.Empty);
		ApproveCommand.NotifyCanExecuteChanged();
		DeclineCommand.NotifyCanExecuteChanged();
		StopCommand.NotifyCanExecuteChanged();
	}

	/// <summary>Takes a newer state of the run (from <c>attention.list</c> or <c>elevated.get</c>). When the run just became final the owner refreshes the item list.</summary>
	public void ApplyRun(ElevatedRunDto run)
	{
		if (_disposed)
			return;

		var wasFinal = IsFinal;
		Run = run;
		ManageTimer();
		if (!wasFinal && IsFinal)
			_onFinished();
	}

	/// <summary>Reads the run again from the daemon (and the last 20 lines of the output while it runs with the expander open).</summary>
	public async Task RefreshAsync()
	{
		try
		{
			var tail = IsOutputExpanded && IsRunning ? Constants.ElevatedRuns.OutputTailLines : 0;
			var result = await _client.ElevatedGetAsync(Run.Id, tail).ConfigureAwait(false);
			_ui.Post(() =>
			{
				ApplyRun(result.Run);
				if (result.OutputTail != null)
					OutputText = result.OutputTail;
			});
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Reading elevated run {Id} failed", Run.Id);
		}
	}

	/// <summary>One second of the card's clock: the elapsed time moves, and every second tick an open Output expander on a running card reads the tail again.</summary>
	public void Tick()
	{
		if (_disposed)
			return;

		_ticks++;
		OnPropertyChanged(nameof(RunningText));
		OnPropertyChanged(nameof(ExpiresText));
		var refreshEvery = (int)(Constants.ElevatedRuns.OutputRefreshInterval / Constants.ElevatedRuns.TickInterval);
		if (IsRunning && IsOutputExpanded && _ticks % refreshEvery == 0)
			_ = RefreshAsync();
	}

	private void ManageTimer()
	{
		if (!IsFinal && _timer == null && !_disposed)
		{
			_timer = _time.CreateTimer(_ => _ui.Post(Tick), null, Constants.ElevatedRuns.TickInterval, Constants.ElevatedRuns.TickInterval);
		}
		else if (IsFinal && _timer != null)
		{
			_timer.Dispose();
			_timer = null;
		}
	}

	/// <summary>
	/// Run as administrator: asks the daemon to approve (the first approve wins), starts exactly the launch description it returns, and reports the start or the failure.
	/// A refused approve (another client was first, the request expired) shows the daemon's text and starts nothing.
	/// </summary>
	[RelayCommand(CanExecute = nameof(CanApprove))]
	private async Task ApproveAsync()
	{
		if (_approving || !IsPending)
			return;

		if (_launcher == null)
		{
			Error = Constants.ElevatedRuns.LauncherUnavailableText;
			return;
		}

		_approving = true;
		Error = null;
		ApproveCommand.NotifyCanExecuteChanged();
		DeclineCommand.NotifyCanExecuteChanged();
		ElevatedLaunchDto launch;
		try
		{
			launch = (await _client.ElevatedApproveAsync(Run.Id)).Launch;
		}
		catch (Exception ex)
		{
			_approving = false;
			Error = ex is TessynRpcException ? ex.Message : string.Format(Constants.ElevatedRuns.RequestFailedFormat, ex.Message);
			_logger.LogInformation("Elevated run {Id}: approve refused: {Message}", Run.Id, ex.Message);
			await RefreshAsync();
			return;
		}

		ApplyRun(Run with { Status = ElevatedRunStatus.Launching });
		IsPromptOpen = true;
		ElevatedLaunchResult result;
		try
		{
			result = await _launcher.LaunchAsync(launch, _cts.Token);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		catch (Exception ex)
		{
			result = ElevatedLaunchResult.Failed(ex.Message);
		}
		finally
		{
			IsPromptOpen = false;
		}

		await ReportAsync(result);
		_approving = false;
		await RefreshAsync();
	}

	private async Task ReportAsync(ElevatedLaunchResult result)
	{
		var id = Run.Id;
		try
		{
			switch (result.Outcome)
			{
				case ElevatedLaunchOutcome.Started:
					await _client.ElevatedLaunchedAsync(id);
					break;
				case ElevatedLaunchOutcome.Declined:
					await _client.ElevatedLaunchFailedAsync(id, declined: true);
					break;
				default:
					await _client.ElevatedLaunchFailedAsync(id, declined: false, result.Reason);
					break;
			}
		}
		catch (TessynRpcException ex) when (result.Outcome == ElevatedLaunchOutcome.Started && IsNotLaunching(ex))
		{
			// A fast command can finish before the report arrives: the daemon already moved on, which is the success the report would have caused.
			_logger.LogInformation("Elevated run {Id}: the run was already past launching when the start was reported", id);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Reporting the launch of elevated run {Id} failed", id);
			Error = string.Format(Constants.ElevatedRuns.RequestFailedFormat, ex.Message);
		}
	}

	private static bool IsNotLaunching(TessynRpcException ex) =>
		ex.Code == RemoteProtocol.ErrorCodes.DaemonError && ex.Message.Contains(Constants.ElevatedRuns.NotLaunchingFragment, StringComparison.OrdinalIgnoreCase);

	/// <summary>Decline: the daemon closes the request as declined; the note (if any) goes into the outcome the agent gets.</summary>
	[RelayCommand(CanExecute = nameof(CanDecline))]
	private async Task DeclineAsync()
	{
		if (_declining || !IsPending)
			return;

		_declining = true;
		Error = null;
		ApproveCommand.NotifyCanExecuteChanged();
		DeclineCommand.NotifyCanExecuteChanged();
		try
		{
			var note = NoteText.Trim();
			await _client.ElevatedDeclineAsync(Run.Id, note.Length == 0 ? null : note);
		}
		catch (Exception ex)
		{
			Error = ex is TessynRpcException ? ex.Message : string.Format(Constants.ElevatedRuns.RequestFailedFormat, ex.Message);
		}
		finally
		{
			_declining = false;
		}

		await RefreshAsync();
	}

	/// <summary>Stop: asks the daemon to stop the run. No confirmation: stopping is the safe direction.</summary>
	[RelayCommand(CanExecute = nameof(CanStop))]
	private async Task StopAsync()
	{
		Error = null;
		try
		{
			await _client.ElevatedStopAsync(Run.Id);
		}
		catch (Exception ex)
		{
			Error = ex is TessynRpcException ? ex.Message : string.Format(Constants.ElevatedRuns.RequestFailedFormat, ex.Message);
		}

		await RefreshAsync();
	}

	/// <summary>Copy command: the exact text of the command on the clipboard.</summary>
	[RelayCommand]
	private void Copy() => _clipboard?.TrySetText(Run.Command);

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		_timer?.Dispose();
		_timer = null;
		_cts.Cancel();
		_cts.Dispose();
	}
}
