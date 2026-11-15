using System;

namespace TessynDesktop;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>The interactive turn engine (P1.I): stream-event vocabulary and the texts it writes into transcripts.</summary>
	public static class Turns
	{
		/// <summary>Types of the parsed claude stream events the engine reacts to.</summary>
		public static class EventTypes
		{
			public const string Assistant = "assistant";
			public const string System = "system";
			public const string Result = "result";

			/// <summary>Not a claude line type: the thinking block of an assistant message, surfaced as an event of its own (P1.I).</summary>
			public const string Thinking = "thinking";

			/// <summary>Not a claude line type: a tool_use block of an assistant message, surfaced as an event of its own (P1.I).</summary>
			public const string ToolUse = "tool_use";

			/// <summary>Not a claude line type: a tool_result block of a user message, surfaced as an event of its own (P1.I).</summary>
			public const string ToolResult = "tool_result";
		}

		/// <summary>Subtypes of <see cref="EventTypes.System"/> events the engine reacts to.</summary>
		public static class EventSubtypes
		{
			public const string Compact = "compact";
			public const string TaskProgress = "task_progress";
			public const string TaskStarted = "task_started";
		}

		/// <summary>Written to the transcript when the user stops a running turn.</summary>
		public const string InterruptedByUserText = "[Interrupted by user]";

		/// <summary>Shown in place of the "thinking" row while a turn is retried with the conversation history.</summary>
		public const string ResumingWithHistoryText = "Resuming session with conversation history...";

		/// <summary>Placeholder stamped on assistant entries when no model or effort was chosen (the "default" option).</summary>
		public const string DefaultOptionText = "default";

		/// <summary>Format of a turn id: a GUID without dashes.</summary>
		public const string TurnIdFormat = "N";

		/// <summary>Name shown for a tool result whose call was not seen.</summary>
		public const string UnknownToolName = "tool";

		/// <summary>Limits of the progress events.</summary>
		public static class Limits
		{
			/// <summary>Longest one-line tool summary; longer text is cut and ends with an ellipsis.</summary>
			public const int ToolSummaryMaxChars = 160;

			/// <summary>Items a turn's event buffer keeps between two flushes before the oldest deltas are dropped.</summary>
			public const int BufferMaxItems = 256;

			/// <summary>Ids of ended turns the event publisher remembers, to ignore their late items.</summary>
			public const int ClosedTurnsRemembered = 512;

			/// <summary>Delta characters a turn's event buffer keeps between two flushes before the oldest deltas are dropped.</summary>
			public const int BufferMaxDeltaChars = 512 * 1024;
		}

		/// <summary>Timings of the progress events.</summary>
		public static class Timings
		{
			/// <summary>Deltas, tool, status and usage notifications of a turn are batched for this long (not the 250 ms of the other events).</summary>
			public static readonly TimeSpan BatchWindow = TimeSpan.FromMilliseconds(50);
		}

		/// <summary>The keys of a tool call's input tried, in order, for its one-line summary.</summary>
		public static readonly string[] ToolSummaryInputKeys = ["command", "file_path", "path", "pattern", "url", "query", "description", "prompt"];

		/// <summary>The marker that ends a cut tool summary.</summary>
		public const string Ellipsis = "...";
	}
}
