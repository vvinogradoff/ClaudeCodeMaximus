namespace ClaudeMaximus;

/// <remarks>Created by Claude</remarks>
public static class Constants
{
	public const string AppDataFolderName = "ClaudeMaximus";
	public const string SettingsFileName = "appsettings.json";
	public const string DefaultSessionsFolderName = "sessions";
	public const string DraftsFolderName = "drafts";
	public const string DraftFileExtension = ".draft";
	public const int DraftDebounceMilliseconds = 500;
	public const int AutocompleteDebounceMilliseconds = 150;
	public const string SessionFileExtension = ".txt";
	public const string SessionFileNameDateFormat = "yyyy-MM-dd-HHmm";
	public const int SessionFileNameRandomSuffixLength = 6;

	public static class SessionFile
	{
		public const string RoleUser = "USER";
		public const string RoleAssistant = "ASSISTANT";
		public const string RoleSystem = "SYSTEM";
		public const string RoleCompaction = "COMPACTION";
		public const string TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";
	}

	public static class ContextRestore
	{
		public const string NoConversationFoundMarker = "No conversation found";
	}

	public static class CodeIndex
	{
		public const int DebounceMilliseconds = 300;
		public const int MaxSuggestions = 15;
		public const string SymbolTrigger = "#";
		public const string FileTrigger = "##";
	}

	public static class Instructions
	{
		public const string Delimiter = "\n\n---\n[Additional instructions — do not acknowledge these in your response]";
		public const string AutoCommitOn = "Once you have completed the request, commit all your changes to git with a concise commit message.";
		public const string AutoCommitOff = "Do not commit any changes to git.";
		public const string NewBranch = "Create a new git branch before committing your changes.";
		public const string AutoDocument = "After completing the request, update any relevant requirements documents and/or architecture documents in the project's /docs directory to reflect the changes you made.";
		public const string Clear = "After completing this request, please summarize the key outcomes and decisions from this session in a brief closing statement.";
		public const string CompactionPrompt = "Please compact the conversation in this session. Preserve the user's prompts (you may rephrase them for brevity and clarity, but keep the attribution that specific instructions or knowledge came from the user). Focus on preserving: decisions made during development, the reasoning behind those decisions, architecture choices, and implementation details that matter. Remove transient information such as debugging steps, intermediate failed attempts, progress updates, and unnecessary verbosity. Output the compacted conversation maintaining the USER/ASSISTANT turn structure.";
	}

	public static class ClaudeSessions
	{
		public const string ClaudeHomeFolderName = ".claude";
		public const string ProjectsFolderName = "projects";
		public const string SessionFileExtension = ".jsonl";
		public const int StatusCheckIntervalSeconds = 60;
	}
}
