namespace ClaudeMaximus;

/// <remarks>Created by Claude</remarks>
public static class Constants
{
	public const string AppDataFolderName = "ClaudeMaximus";
	public const string SettingsFileName = "appsettings.json";
	public const string DefaultSessionsFolderName = "sessions";
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
}
