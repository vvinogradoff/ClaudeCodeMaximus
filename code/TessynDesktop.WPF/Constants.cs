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
		public const string BrowserFolder = "browser";
		public const string ClientsFolder = "clients";
		public const string ClientStateFileName = "wpf.json";
		public const string LogFilePattern = "wpf-.txt";
		public const string WebAssetsFolder = "Web";
		public const string BadFileSuffix = ".bad";
		public const string TranscriptPageFileName = "transcript.html";
		public const string PreviewPageFileName = "preview.html";
		public const string PreviewCacheFolder = "preview-cache";
	}

	/// <summary>The kinds of copy the transcript page asks the host for (FR.2.13); the page uses the same words.</summary>
	public static class CopyKinds
	{
		public const string Text = "text";
		public const string Markdown = "markdown";
	}

	/// <summary>Hosts the pages are served from (virtual host mappings).</summary>
	public static class Hosts
	{
		public const string AppHost = "app.local";
		public const string PreviewHost = "preview.local";

		/// <summary>The transcript page's only image host: a virtual name the host answers itself (the media proxy); it is never resolved.</summary>
		public const string MediaHost = "media.local";

		/// <summary>The address WebView2 reports for the load of an iframe's <c>srcdoc</c> document (the preview's html frame, FR.26.8).</summary>
		public const string SrcdocUri = "about:srcdoc";
	}

	/// <summary>Texts the Focus view and the preview pane show; tests refer to them too.</summary>
	public static class Texts
	{
		public const string NoOpenItems = "Nothing needs your attention in this session.";
		public const string NoFocusSession = "Open a session to see what needs your attention.";
		public const string DecisionsHeaderFormat = "Decisions ({0})";
		public const string ClosedHeaderFormat = "Closed ({0})";

		/// <summary>FR.23.42: the header of the Running group at the top of the Focus view.</summary>
		public const string RunningHeaderFormat = "Running ({0})";

		/// <summary>FR.23.42: the chip of an approved elevated run in progress, and the key the chip style reads (a neutral colour, not the Ask one).</summary>
		public const string RunningChipText = "Running";
		public const string RunningChipKey = "running";
		public const string OutcomeAnswered = "Answered";
		public const string OutcomeResolved = "Resolved";
		public const string OutcomeDismissed = "Dismissed";
		public const string OutcomeFormat = "{0}: {1}";
		public const string AcceptedRecommendation = "Accepted your recommendation: ";
		public const string RecommendedTooltip = "Recommended";
		public const string OptionTooltip = "Use this as your answer";
		public const string FocusPathNotPreviewable = "This file cannot be previewed.";

		/// <summary>Shown when a file could not be shown in any preview target (no docking area, or the panel could not be created).</summary>
		public const string FilePreviewUnavailable = "This file cannot be previewed here.";
		public const string EntryGone = "The referenced entry no longer exists in that session.";
		public const string PreviewTooLarge = "This file is too large to preview";
		/// <summary>The part of the daemon's FileAccess refusal text ("That file cannot be read remotely. ...") that tells a refusal by the access rules from a missing file or a wrong path.</summary>
		public const string FileAccessRefusalFragment = "cannot be read remotely";

		public const string PreviewUnsupported = "This file type has no preview.";
		public const string PreviewTiffUndecodable = "This TIFF image could not be decoded.";
		public const string PreviewTruncated = "Showing the first part of a large file.";
		public const string PdfHint = "PDF files open in your default PDF app.";
		public const string VideoOpensExternallyHint = "This video type plays in your default media player: use Open in default app.";
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

		public static readonly IReadOnlySet<string> Image = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff" };

		/// <summary>Decoded by the host and shown as PNG (plan D9).</summary>
		public static readonly IReadOnlySet<string> Tiff = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".tif", ".tiff" };

		public const string PngExtension = ".png";

		public static readonly IReadOnlySet<string> Video = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".webm", ".mov", ".m4v", ".ogv" };

		/// <summary>Video types a local path may show a first-frame preview for (FR.26.6); the shell makes the frame with the installed codecs. Wider than <see cref="Video"/>, which is what the preview pane and remote links play.</summary>
		public static readonly IReadOnlySet<string> InlineVideo = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".webm", ".mkv", ".avi", ".m4v", ".wmv" };

		/// <summary>Top-level domains that make the first part of a relative-looking path a web host (<c>example.com/page.html</c>), so it is not marked as a relative file path (FR.26.7).</summary>
		public static readonly IReadOnlySet<string> WebTopLevelDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"com", "org", "net", "io", "app", "dev", "ai", "co", "gov", "edu", "info", "biz", "me", "us", "uk", "de", "fr", "ru", "ca", "au", "jp", "nl", "xyz"
		};

		/// <summary>The host a link may name without a top-level domain; a relative-looking path that starts with it is a web address (FR.26.7).</summary>
		public const string LocalWebHost = "localhost";

		/// <summary>Extensions that make a bare name in backticks (<c>README.md</c>) a file; any other (<c>obj.method</c>, <c>os.path</c>) is code (FR.26.7).</summary>
		public static readonly IReadOnlySet<string> KnownBareNameExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".md", ".markdown", ".txt", ".json", ".yml", ".yaml", ".toml", ".xml", ".csv", ".cs", ".csproj", ".sln", ".ts", ".tsx", ".js", ".jsx", ".mjs",
			".py", ".rb", ".go", ".rs", ".java", ".kt", ".swift", ".c", ".h", ".cpp", ".hpp", ".css", ".scss", ".html", ".htm", ".sh", ".ps1", ".bat", ".cmd",
			".sql", ".ini", ".cfg", ".config", ".props", ".targets", ".xaml", ".razor", ".log", ".lock", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
			".tif", ".tiff", ".svg", ".pdf", ".mp4", ".mov", ".webm", ".mkv", ".avi", ".m4v", ".wmv"
		};

		public static readonly IReadOnlySet<string> Pdf =new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf" };

		/// <summary>Html pages: rendered static in a sandboxed frame, scripts never run (FR.26.8); the source can be shown as text.</summary>
		public static readonly IReadOnlySet<string> Html = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".html", ".htm" };

		/// <summary>Shown as monospace text, markup and script types included (svg, ps1, ...): displayed, never rendered or run.</summary>
		public static readonly IReadOnlySet<string> Text = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".txt", ".log", ".json", ".jsonl", ".xml", ".yml", ".yaml", ".toml", ".ini", ".cfg", ".config", ".csv", ".tsv",
			".cs", ".csproj", ".sln", ".props", ".targets", ".xaml", ".axaml", ".razor", ".cshtml",
			".js", ".mjs", ".ts", ".tsx", ".jsx", ".css", ".scss", ".svg",
			".py", ".rb", ".go", ".rs", ".java", ".kt", ".swift", ".c", ".h", ".cpp", ".hpp", ".php", ".sql", ".sh", ".ps1", ".psm1", ".bat", ".cmd",
			".diff", ".patch",
		};

		/// <summary>
		/// The only types that may be opened in the default app: documents, media and html pages (decision A33: the page's scripts run in the
		/// default browser). Executables and scripts never leave the preview.
		/// </summary>
		public static readonly IReadOnlySet<string> ExternallyOpenable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".md", ".markdown", ".txt", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".mp4", ".webm", ".mov", ".m4v", ".ogv", ".mkv", ".avi", ".wmv", ".pdf", ".html", ".htm",
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
		public const int PreviewChunkBytes = 512 * 1024;
		public const long MaxPreviewTextBytes = 2L * 1024 * 1024;
		public const long MaxPreviewImageBytes = 25L * 1024 * 1024;

		/// <summary>A TIFF whose first frame has more pixels than this (100 megapixels) is refused before it is decoded: the file is small, the bitmap would not be.</summary>
		public const long MaxTiffPixels = 100_000_000L;
		public const long MaxPreviewVideoBytes = 500L * 1024 * 1024;
		/// <summary>The most a video may weigh for the host to fetch it only to make its first frame (FR.26.6); larger ones show "Preview unavailable" and still open on Ctrl+click.</summary>
		public const long MaxInlineVideoBytes = 150L * 1024 * 1024;
		/// <summary>Longest side, in pixels, of the first frame the shell is asked for.</summary>
		public const int InlineVideoFramePixels = 720;
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
