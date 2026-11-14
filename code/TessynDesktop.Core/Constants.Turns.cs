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
	}
}
