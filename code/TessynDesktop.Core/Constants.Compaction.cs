namespace TessynDesktop;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Texts of the compaction outcome lines shown (and, for the remote path, appended) as SYSTEM entries (FR.11.6).</summary>
	public static class Compaction
	{
		public const string NothingToCompactMessage = "[Nothing to compact]";
		public const string CompactedMessage = "[Session compacted and JSONL detached — next prompt will use the compacted text session as context]";
		public const string EmptyResultMessage = "[Compaction returned no content — session left unchanged]";

		/// <summary>Line shown when Stop interrupted a compaction (the session file is left unchanged).</summary>
		public const string StoppedMessage = "[Compaction stopped — session left unchanged]";

		/// <summary>Format of the failure line; the argument is the error text.</summary>
		public const string FailedMessageFormat = "[Compaction failed — session left unchanged: {0}]";

		/// <summary>Location of the glossary file looked up under the project's working directory and injected into the prompt.</summary>
		public const string GlossaryRelativeFolder = "docs";
		public const string GlossaryFileName = "glossary.md";
	}
}
