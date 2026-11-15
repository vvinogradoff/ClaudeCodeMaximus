namespace TessynDesktop.WPF.Infrastructure;

/// <summary>
/// Command-line overrides, mainly for automated tests: <c>--daemon-file &lt;path&gt;</c> (use this daemon.json instead of the
/// default one) and <c>--data-root &lt;path&gt;</c> (logs, client state and WebView2 data go there). A custom data root also gets its
/// own single-instance mutex, so a test run never collides with the user's running app. <c>--test-window</c> keeps the window off the
/// user's screen and focus (see <see cref="TestWindow"/>).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class StartupOptions
{
	public const string DaemonFileArgument = "--daemon-file";
	public const string DataRootArgument = "--data-root";
	public const string TestWindowArgument = "--test-window";
	public const string TestDropArgument = "--test-drop";
	public const string TestDialogArgument = "--test-dialog";
	public const string TestFolderArgument = "--test-folder";

	public string? DaemonFile { get; init; }

	public string? DataRoot { get; init; }

	/// <summary>
	/// Automated-test mode: the main window is shown without being activated (no focus stealing), outside the visible monitors and
	/// not in the taskbar, and it neither restores nor saves its placement. It still renders, so the page's first paint happens.
	/// </summary>
	public bool TestWindow { get; init; }

	/// <summary>
	/// Test seam for end-to-end tests (only honoured together with <c>--test-window</c>; the repeatable <c>--test-drop &lt;path&gt;</c>): once the first session is shown,
	/// these paths are handed to the prompt input exactly as a drop of those files would hand them over. Real OLE drag-and-drop cannot be driven without
	/// moving the user's mouse, so the test starts after the drop target (<c>FileDropReader</c>, covered by view tests) and exercises everything behind it.
	/// </summary>
	public IReadOnlyList<string> TestDropPaths { get; init; } = [];

	/// <summary>
	/// Test seam (only honoured together with <c>--test-window</c>; repeatable <c>--test-dialog &lt;answer&gt;</c>): dialogs open no window but are answered in order
	/// from these entries (see <c>ScriptedDialogPresenter</c>); with none left they are cancelled. Without it an E2E test that triggers a dialog would hang on a modal window.
	/// </summary>
	public IReadOnlyList<string> TestDialogAnswers { get; init; } = [];

	/// <summary>Test seam (only with <c>--test-window</c>; repeatable <c>--test-folder &lt;path&gt;</c>): the folder dialog opens no window and answers with these folders in order (see <c>ScriptedFolderPicker</c>).</summary>
	public IReadOnlyList<string> TestFolderAnswers { get; init; } = [];

	/// <summary>The single-instance mutex name for this run.</summary>
	public string MutexName => DataRoot == null
		? Constants.Identity.SingleInstanceMutexName
		: Constants.Identity.SingleInstanceMutexName + "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(DataRoot).ToLowerInvariant())))[..16];

	public static StartupOptions Parse(IReadOnlyList<string> args)
	{
		string? daemonFile = null;
		string? dataRoot = null;
		var testDrops = new List<string>();
		var testDialogs = new List<string>();
		var testFolders = new List<string>();
		for (var i = 0; i < args.Count - 1; i++)
		{
			if (string.Equals(args[i], DaemonFileArgument, StringComparison.OrdinalIgnoreCase))
				daemonFile = args[++i];
			else if (string.Equals(args[i], DataRootArgument, StringComparison.OrdinalIgnoreCase))
				dataRoot = args[++i];
			else if (string.Equals(args[i], TestDropArgument, StringComparison.OrdinalIgnoreCase))
				testDrops.Add(args[++i]);
			else if (string.Equals(args[i], TestDialogArgument, StringComparison.OrdinalIgnoreCase))
				testDialogs.Add(args[++i]);
			else if (string.Equals(args[i], TestFolderArgument, StringComparison.OrdinalIgnoreCase))
				testFolders.Add(args[++i]);
		}

		var testWindow = args.Any(a => string.Equals(a, TestWindowArgument, StringComparison.OrdinalIgnoreCase));
		return new StartupOptions { DaemonFile = daemonFile, DataRoot = dataRoot, TestWindow = testWindow, TestDropPaths = testWindow ? testDrops : [], TestDialogAnswers = testWindow ? testDialogs : [], TestFolderAnswers = testWindow ? testFolders : [] };
	}
}
