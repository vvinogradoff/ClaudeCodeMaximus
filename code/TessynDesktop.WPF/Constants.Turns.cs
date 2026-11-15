namespace TessynDesktop.WPF;

/// <summary>Limits and texts of the turn loop (W1a): the live row below the transcript and the selectors of the command bar.</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Bounds of the in-flight turn preview; the persisted entry is the source of truth, so a preview may be cut.</summary>
	public static class TurnLimits
	{
		/// <summary>Newest characters of streamed text kept per turn.</summary>
		public const int PreviewTextChars = 24000;

		/// <summary>Newest characters of streamed thinking kept per turn.</summary>
		public const int PreviewThinkingChars = 4000;

		/// <summary>Most tool calls listed in the live row (the newest).</summary>
		public const int PreviewTools = 12;

		/// <summary>Sessions that are not open and ended their last turn with an error keep that error row; older ones are dropped.</summary>
		public const int RememberedErrorSessions = 20;

		/// <summary>Characters of a selector's label shown before its start is cut (the full label is the tooltip).</summary>
		public const int SelectorLabelChars = 30;

		/// <summary>Streamed deltas reach the page at most this often: bursts are merged, the UI thread does one small update per window.</summary>
		public static readonly TimeSpan LiveFlushInterval = TimeSpan.FromMilliseconds(80);
	}

	/// <summary>Words of the live row and the selectors.</summary>
	public static class TurnTexts
	{
		public const string Thinking = "Thinking...";
		public const string Responding = "Responding...";
		public const string Working = "Working...";
		public const string Waiting = "Waiting in the queue";
		public const string QueuedRow = "Message {0} is waiting for the running turn";
		public const string GapMarker = " [...] ";
		public const string ResumingSession = "Resuming session...";
		public const string RetryingAuth = "Retrying after a sign-in refresh...";
		public const string Retrying = "Retrying...";
		public const string DefaultOption = "default";
		public const string SystemMarker = "[Sys]";
		public const string TruncationMark = "...";
		public const string StopFailed = "Stop failed: ";
		public const string SendFailed = "Send failed: ";
		public const string NotAccepted = "The daemon did not accept the message.";
	}
}
