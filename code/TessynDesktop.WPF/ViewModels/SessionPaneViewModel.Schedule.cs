using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The schedules of the session pane (W8, FR.14): the list behind the header indicator, and the delayed send ("Send later…", right-click on Send). The message is
/// scheduled with <c>schedules.add</c>, together with the profile, model and effort picked now, and leaves the box only when the daemon accepted it; the daemon
/// sends it as a turn with those picks when the time comes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel
{
	/// <summary>The schedules of the open session.</summary>
	public ScheduleViewModel Schedules { get; private set; } = null!;

	/// <summary>The clock of the delayed send (tests set it).</summary>
	public TimeProvider ScheduleClock { get; set; } = TimeProvider.System;

	/// <summary>The time zone the delayed send's answer is read in (tests set it).</summary>
	public TimeZoneInfo ScheduleZone { get; set; } = TimeZoneInfo.Local;

	private void InitializeSchedules()
	{
		Schedules = new ScheduleViewModel(_client, _ui, _logger);
		PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(NodeId))
				Schedules.SetSession(NodeId);
			else if (e.PropertyName == nameof(HasSession))
				SendLaterCommand.NotifyCanExecuteChanged();
		};
	}

	private void DisposeSchedules() => Schedules.Dispose();

	private bool CanSendLater() => HasSession;

	/// <summary>Asks when, then schedules the text of the box as a delayed send. The box is emptied only when the schedule was added.</summary>
	[RelayCommand(CanExecute = nameof(CanSendLater))]
	private async Task SendLaterAsync()
	{
		var id = NodeId;
		var dialogs = Dialogs;
		if (id == null || dialogs == null)
			return;

		if (!Schedules.IsSupported)
		{
			Notice = Constants.Schedules.NotSupported;
			return;
		}

		var text = InputText.Trim();
		if (text.Length == 0)
		{
			Notice = Constants.Schedules.NothingToSend;
			return;
		}

		// New Branch and Auto-Compact are one-shot flags of a send made now (FR.11.4, FR.11.6); a schedule cannot carry them. Refused with a notice, not dropped silently.
		// Attached files need nothing here: in file mode their paths are already lines of the text, so they travel with it.
		if (Instruction?.NewBranch == true || Instruction?.AutoCompact == true)
		{
			Notice = Constants.Schedules.OneShotsNotScheduled;
			return;
		}

		var answer = await dialogs.InputAsync(new InputDialogRequest(
			Constants.Schedules.DialogTitle, Constants.Schedules.DialogPrompt, Constants.Schedules.DefaultAnswer, Constants.Schedules.DialogConfirm,
			value => ScheduleTimeParser.TryParse(value, ScheduleClock.GetUtcNow(), ScheduleZone, out var error) == null ? error : null));
		if (answer == null || id != NodeId)
			return;

		// The dialog checked the answer against the clock of that moment; read it again against now.
		var time = ScheduleTimeParser.TryParse(answer, ScheduleClock.GetUtcNow(), ScheduleZone, out var problem);
		if (time == null)
		{
			Error = Constants.Schedules.ScheduleFailed + problem;
			return;
		}

		// A14: the delayed send keeps what the user picked now, as a queued send does. A field not picked is not sent (tdcp-spec 9.8).
		var picks = PicksOfOpenSession();
		try
		{
			await _client.SchedulesAddAsync(id, text, time.Kind, time.FireAtUtc, note: Constants.Schedules.DelayedSendNote,
				profileId: picks.ProfileId, modelId: picks.ModelId, effort: picks.EffortId);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "schedules.add failed for {NodeId}", id);
			Error = Constants.Schedules.ScheduleFailed + ex.Message;
			return;
		}

		Error = null;
		Notice = string.Format(Constants.Schedules.ScheduledNotice, TimeZoneInfo.ConvertTime(time.FireAtUtc, ScheduleZone).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
		if (InputText.Trim() == text)
			SetInput(string.Empty);

		await Schedules.RefreshAsync();
	}
}
