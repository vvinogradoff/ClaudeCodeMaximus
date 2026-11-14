namespace TessynDesktop.WPF;

/// <summary>Literals with a meaning beyond the code block that uses them, grouped by role (code_standards.md).</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Names of files and folders below the data root.</summary>
	public static class Paths
	{
		public const string DataFolderName = "TessynDesktop";
		public const string LogsFolder = "logs";
		public const string WebView2Folder = "webview2";
		public const string ClientsFolder = "clients";
		public const string ClientStateFileName = "wpf.json";
		public const string LogFilePattern = "wpf-.txt";
		public const string WebAssetsFolder = "Web";
		public const string BadFileSuffix = ".bad";
		public const string TranscriptPageFileName = "transcript.html";
	}

	/// <summary>Sizes and counts.</summary>
	public static class Limits
	{
		public const int LogFileSizeBytes = 50 * 1024 * 1024;
		public const int LogRetainedFiles = 14;
		public const int EntriesPageSize = 100;
		public const int MaxCachedTranscripts = 4;
		public const int MaxSearchWorkerLookups = 50;
		public const int MinWindowVisiblePixels = 80;
		public const int MinWindowWidth = 640;
		public const int MinWindowHeight = 400;
		public const double DefaultTreeWidth = 320;
		public const double MinTreeWidth = 160;
		public const double MaxTreeWidth = 2000;
	}

	/// <summary>Durations.</summary>
	public static class Timeouts
	{
		public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);
		public static readonly TimeSpan StateSaveDebounce = TimeSpan.FromSeconds(1);
		public static readonly TimeSpan DaemonDiscoveryRetry = TimeSpan.FromSeconds(3);
		public static readonly TimeSpan WatchdogPoll = TimeSpan.FromMilliseconds(100);
		public static readonly TimeSpan UiStallThreshold = TimeSpan.FromMilliseconds(250);
		public static readonly TimeSpan WebViewReady = TimeSpan.FromSeconds(30);
	}

	/// <summary>Process-wide identities.</summary>
	public static class Identity
	{
		public const string SingleInstanceMutexName = @"Local\TessynDesktop.WPF.SingleInstance";
		public const string ClientName = "TessynDesktop.WPF";
		public const string ClientVersion = "0.1.0";
		public const string AppTitle = "Tessyn";
	}
}
