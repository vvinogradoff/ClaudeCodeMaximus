namespace TessynDesktop.WPF;

/// <summary>Names and texts of the schedules UI: the delayed send, the indicator in the session header and on the tree rows, and the schedule list (W8, FR.14).</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	public static class Schedules
	{
		public const string SendLaterCommandId = "session.sendLater";
		public const string SendLaterTitle = "Send later…";
		public const string SendLaterGesture = "Ctrl+Shift+Enter";

		public const string DialogTitle = "Send later";
		public const string DialogPrompt = "When should the message be sent? A delay (30m, 2h, 1d, 1h30m; a bare number is minutes) or a time (14:30, or 2026-11-20 09:30).";
		public const string DialogConfirm = "Schedule";
		public const string DefaultAnswer = "30m";

		/// <summary>The note a delayed send carries; the same text as the Avalonia client's, so both list it alike.</summary>
		public const string DelayedSendNote = "Delayed send";

		public const int IndicatorOrder = 20;

		/// <summary>After the selectors (10) and the toggles (20) of the command bar.</summary>
		public const int SendLaterButtonOrder = 30;
		public const string SendLaterButtonName = "Send later";
		public const string SendLaterToolTip = "Schedule the message instead of sending it now (also: right-click Send, Ctrl+Shift+Enter)";

		public const string IndicatorName = "Schedules";
		public const string CancelPrefix = "Cancel schedule ";
		public const string ListName = "Schedule list";
		public const string MailboxName = "Mailbox deliveries";
		public const string ScheduleRowPrefix = "Schedule ";

		public const string NothingToSend = "Type the message to send later first.";
		public const string NotSupported = "This daemon does not offer schedules.";
		public const string ScheduledNotice = "Scheduled for {0}";
		public const string ScheduleFailed = "The message could not be scheduled: ";
		public const string CancelFailed = "The schedule could not be cancelled: ";
		public const string LoadFailed = "The schedules could not be loaded: ";

		public const string MailboxLabel = "Mailbox delivery";
		public const string DelayedLabel = "Delayed send";
		public const string ScheduledLabel = "Scheduled";
		public const string CronLabel = "Repeating";
		public const string NoTime = "no next time";
		public const int PromptPreviewLength = 80;
		public const string TreeIndicatorTip = "This session has a schedule";

		/// <summary>The most a schedule may lie ahead (the daemon's own limit is ten years).</summary>
		public static readonly TimeSpan MaxAhead = TimeSpan.FromDays(3650);
	}
}
