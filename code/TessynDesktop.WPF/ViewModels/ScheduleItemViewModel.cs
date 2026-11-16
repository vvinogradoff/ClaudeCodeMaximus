using System.Globalization;
using System.Windows.Input;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>One schedule of the open session as the list shows it. A mailbox delivery (FR.15) belongs to an async worker and is shown read-only.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScheduleItemViewModel
{
	public ScheduleItemViewModel(ScheduleDto schedule, TimeZoneInfo zone, ICommand? cancel = null)
	{
		Schedule = schedule;
		Cancel = cancel;
		var first = schedule.Prompt.Split('\n', 2)[0].Trim();
		PromptPreview = first.Length > Constants.Schedules.PromptPreviewLength ? first[..Constants.Schedules.PromptPreviewLength] + "…" : first;
		WhenText = schedule.FireAtUtc is { } at
			? TimeZoneInfo.ConvertTime(at, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
			: Constants.Schedules.NoTime;
		if (schedule.Kind == ScheduleKindDto.Cron && !string.IsNullOrEmpty(schedule.Cron))
			WhenText = $"{schedule.Cron} · next {WhenText}";
	}

	public ScheduleDto Schedule { get; }

	/// <summary>Cancels this schedule (the list's command, with the item as its parameter); null in a list that cannot cancel.</summary>
	public ICommand? Cancel { get; }

	public string Id => Schedule.Id;

	public bool IsMailbox => Schedule.IsMailbox;

	/// <summary>A user schedule can be cancelled; a mailbox delivery cannot from here.</summary>
	public bool CanCancel => !Schedule.IsMailbox;

	public string PromptPreview { get; }

	public string WhenText { get; }

	public string Note => Schedule.Note;

	public string KindText => Schedule.IsMailbox ? Constants.Schedules.MailboxLabel
		: Schedule.Kind == ScheduleKindDto.Cron ? Constants.Schedules.CronLabel
		: Schedule.Note == Constants.Schedules.DelayedSendNote ? Constants.Schedules.DelayedLabel
		: Constants.Schedules.ScheduledLabel;

	public string AutomationName => Constants.Schedules.ScheduleRowPrefix + Id;

	public string CancelAutomationName => Constants.Schedules.CancelPrefix + Id;
}
