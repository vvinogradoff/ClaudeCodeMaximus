namespace TessynDesktop.WPF;

public static partial class Constants
{
	/// <summary>The elevated-run card of the Focus panel (FR.23.32): its texts, its limits and the UI Automation names the E2E tests read.</summary>
	public static class ElevatedRuns
	{
		// UI Automation names (FR.23.32): E2E tests find the elements by them.
		public const string ReasonName = "Reason";
		public const string WorkingDirectoryName = "Working directory";
		public const string CommandName = "Command";
		public const string RunAsAdministratorName = "Run as administrator";
		public const string DeclineName = "Decline";
		public const string StopName = "Stop";
		public const string CopyCommandName = "Copy command";
		public const string NoteName = "Note for the agent (optional)";
		public const string OutputName = "Output";
		public const string OutputTextName = "Output text";
		public const string LogPathName = "Log path";
		public const string StateTextName = "Elevated run state";
		public const string OutcomeTextName = "Elevated run outcome";
		public const string ErrorTextName = "Elevated run error";

		// The labels above the facts. Their UI Automation names differ from the facts' own, so a search by name finds the fact.
		public const string ReasonLabel = "Reason";
		public const string WorkingDirectoryLabel = "Working directory";
		public const string CommandLabel = "Command";
		public const string ReasonLabelName = "Reason heading";
		public const string WorkingDirectoryLabelName = "Working directory heading";
		public const string CommandLabelName = "Command heading";

		// Texts.
		public const string RequestedByFormat = "Requested by {0} ({1})";
		public const string RequestedByWorkerFormat = "Requested by worker {0} ({1}) of this session";
		public const string CopyCommandText = "Copy command";
		public const string RunText = "Run as administrator";
		public const string DeclineText = "Decline";
		public const string StopText = "Stop";
		public const string NoteWatermark = "Note for the agent (optional)";
		public const string OutputHeader = "Output";
		public const string LogPathFormat = "Log: {0}";
		public const string TimeLimitFormat = "Time limit: {0} minutes";
		public const string ExpiresFormat = "Expires {0}";
		public const string WaitingForPromptText = "Waiting for the Windows administrator prompt...";
		public const string StartingText = "Starting...";
		public const string RunningFormat = "Running as administrator since {0} ({1}, limit {2} min)";
		public const string LauncherUnavailableText = "This window cannot start administrator runs.";
		public const string RequestFailedFormat = "The request failed: {0}";

		// Closed-card outcome lines (FR.23.32).
		public const string OutcomeCompleted = "Completed";
		public const string OutcomeCompletedExitFormat = "Completed: exit code {0}";
		public const string OutcomeCompletedExitDurationFormat = "Completed: exit code {0}, {1}";
		public const string OutcomeDeclined = "Declined";
		public const string OutcomeDeclinedAtPrompt = "Declined at the Windows prompt";
		public const string OutcomeFailedFormat = "Failed to start: {0}";
		public const string OutcomeFailed = "Failed to start";
		public const string OutcomeTimedOutFormat = "Timed out after {0} min";
		public const string OutcomeStoppedByUser = "Cancelled (stopped by the user)";
		public const string OutcomeWithdrawnByAgent = "Cancelled (withdrawn by the agent)";
		public const string OutcomeExpired = "Expired";

		/// <summary>The daemon's refusal of <c>elevated.launched</c> for a run that already moved on (a fast command finishes before the report arrives): a success for the card.</summary>
		public const string NotLaunchingFragment = "not launching";

		// Limits.
		/// <summary>The command block shows at most this many lines, then scrolls (FR.23.32).</summary>
		public const int CommandMaxLines = 12;

		/// <summary>The output expander shows the last this many lines (FR.23.32).</summary>
		public const int OutputTailLines = 20;

		/// <summary>The output refreshes this often while the expander is open (FR.23.32).</summary>
		public static readonly TimeSpan OutputRefreshInterval = TimeSpan.FromSeconds(2);

		/// <summary>The card's own clock: elapsed time and the expiry text follow it.</summary>
		public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

		/// <summary>Longest decline note the daemon accepts.</summary>
		public const int MaxNoteLength = 500;
	}
}
