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
		public const string PreviewPageFileName = "preview.html";
		public const string PreviewCacheFolder = "preview-cache";
	}

	/// <summary>Hosts the pages are served from (virtual host mappings).</summary>
	public static class Hosts
	{
		public const string AppHost = "app.local";
		public const string PreviewHost = "preview.local";

		/// <summary>The transcript page's only image host: a virtual name the host answers itself (the media proxy); it is never resolved.</summary>
		public const string MediaHost = "media.local";
	}

	/// <summary>Texts the Focus view and the preview pane show; tests refer to them too.</summary>
	public static class Texts
	{
		public const string NoOpenItems = "Nothing needs your attention in this session.";
		public const string DecisionsHeaderFormat = "Decisions ({0})";
		public const string ClosedHeaderFormat = "Closed ({0})";
		public const string OutcomeAnswered = "Answered";
		public const string OutcomeResolved = "Resolved";
		public const string OutcomeDismissed = "Dismissed";
		public const string OutcomeFormat = "{0}: {1}";
		public const string AcceptedRecommendation = "Accepted your recommendation: ";
		public const string EntryGone = "The referenced entry no longer exists in that session.";
		public const string PreviewTooLarge = "This file is too large to preview";
		/// <summary>The part of the daemon's FileAccess refusal text ("That file cannot be read remotely. ...") that tells a refusal by the access rules from a missing file or a wrong path.</summary>
		public const string FileAccessRefusalFragment = "cannot be read remotely";

		public const string PreviewUnsupported = "This file type has no preview.";
		public const string PreviewTruncated = "Showing the first part of a large file.";
		public const string PdfHint = "PDF files open in your default PDF app.";
		public const string AttachedFilePrefix = "Attached file: ";
		public const string AttachFolderRefused = "a folder cannot be attached; drop the files inside it instead";
		public const string AttachNeedsSession = "Select a session first: files are attached to a session.";
		public const string AttachBusy = "Files are still being attached. Wait for them or cancel.";
		public const string AttachCancelled = "Attaching cancelled.";
		public const string AttachFailedHeading = "Could not attach:";
		public const string InsertFailedHeading = "Could not insert:";
		public const string HiddenCharactersRefused = "the name contains control or invisible characters";
		public const string StoredPathRefused = "the stored path contains control or invisible characters, so it was not inserted";
	}

	/// <summary>
	/// File types by what the client may do with them. A preview only ever displays content; nothing listed here is executed.
	/// </summary>
	public static class FileTypes
	{
		public static readonly IReadOnlySet<string> Markdown = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown" };

		public static readonly IReadOnlySet<string> Image = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

		public static readonly IReadOnlySet<string> Video = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".webm", ".mov", ".m4v", ".ogv" };

		public static readonly IReadOnlySet<string> Pdf = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf" };

		/// <summary>Shown as monospace text, markup and script types included (svg, html, ps1, ...): displayed, never rendered or run.</summary>
		public static readonly IReadOnlySet<string> Text = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".txt", ".log", ".json", ".jsonl", ".xml", ".yml", ".yaml", ".toml", ".ini", ".cfg", ".config", ".csv", ".tsv",
			".cs", ".csproj", ".sln", ".props", ".targets", ".xaml", ".axaml", ".razor", ".cshtml",
			".js", ".mjs", ".ts", ".tsx", ".jsx", ".css", ".scss", ".html", ".htm", ".svg",
			".py", ".rb", ".go", ".rs", ".java", ".kt", ".swift", ".c", ".h", ".cpp", ".hpp", ".php", ".sql", ".sh", ".ps1", ".psm1", ".bat", ".cmd",
			".diff", ".patch",
		};

		/// <summary>The only types that may be opened in the default app: documents and media. Executables and scripts never leave the preview.</summary>
		public static readonly IReadOnlySet<string> ExternallyOpenable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".md", ".markdown", ".txt", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".mp4", ".webm", ".mov", ".m4v", ".ogv", ".pdf",
		};

		/// <summary>Remote media links by extension (FR.26.4); svg is left out on purpose.</summary>
		public static readonly IReadOnlySet<string> RemoteImage = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".avif" };
	}

	/// <summary>Sizes and counts.</summary>
	public static class Limits
	{
		public const int LogFileSizeBytes = 50 * 1024 * 1024;
		public const int LogRetainedFiles = 14;
		public const int EntriesPageSize = 100;
		public const int MaxCachedTranscripts = 4;
		public const int MaxListedAttachFailures = 5;
		public const int MaxSearchWorkerLookups = 50;
		public const int MinWindowVisiblePixels = 80;
		public const int MinWindowWidth = 640;
		public const int MinWindowHeight = 400;
		public const double DefaultTreeWidth = 320;
		public const double MinTreeWidth = 160;
		public const double OffScreenCoordinate = -32000;
		public const double MaxTreeWidth = 2000;
		public const double DefaultSidePaneWidth = 400;
		public const double MinSidePaneWidth = 280;
		public const int PreviewChunkBytes = 512 * 1024;
		public const long MaxPreviewTextBytes = 2L * 1024 * 1024;
		public const long MaxPreviewImageBytes = 25L * 1024 * 1024;
		public const long MaxPreviewVideoBytes = 500L * 1024 * 1024;
		public const long MaxPreviewPdfBytes = 100L * 1024 * 1024;
		public const int MaxMediaPreviewsPerEntry = 6;
		public const int MaxPathChipsPerEntry = 8;
		public const int MaxPathChipLength = 400;
		public const int MaxMediaRedirects = 3;
		public const long MaxRemoteImageBytes = 10L * 1024 * 1024;
		public const long MaxRemoteVideoBytes = 200L * 1024 * 1024;
		public const int MaxCachedRemoteImages = 24;
		public const long MaxImageCacheBytes = 100L * 1024 * 1024;
		public const long MaxVideoBytesPerLoad = 1024L * 1024 * 1024;
		public const int MaxConcurrentVideoDownloads = 2;
		public const int MaxMarkdownChars = 200_000;
		public const int MaxPreviewChunks = 4096;
		public const int MaxCatchUpPages = 2;
	}

	/// <summary>Durations.</summary>
	public static class Timeouts
	{
		public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);
		/// <summary>Rebuilds and reloads of the session lists while states stream in.</summary>
		public static readonly TimeSpan ListRebuildThrottle = TimeSpan.FromSeconds(1);
		public static readonly TimeSpan RecencyRefresh = TimeSpan.FromSeconds(60);
		public static readonly TimeSpan StateSaveDebounce = TimeSpan.FromSeconds(1);
		public static readonly TimeSpan DaemonDiscoveryRetry = TimeSpan.FromSeconds(3);
		public static readonly TimeSpan WatchdogPoll = TimeSpan.FromMilliseconds(100);
		public static readonly TimeSpan UiStallThreshold = TimeSpan.FromMilliseconds(250);
		public static readonly TimeSpan WebViewReady = TimeSpan.FromSeconds(30);
		public static readonly TimeSpan RemoteImage = TimeSpan.FromSeconds(20);
		public static readonly TimeSpan RemoteVideo = TimeSpan.FromMinutes(5);
		public static readonly TimeSpan RemoteConnect = TimeSpan.FromSeconds(10);
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
