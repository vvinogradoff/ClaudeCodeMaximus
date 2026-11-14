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

	public string? DaemonFile { get; init; }

	public string? DataRoot { get; init; }

	/// <summary>
	/// Automated-test mode: the main window is shown without being activated (no focus stealing), outside the visible monitors and
	/// not in the taskbar, and it neither restores nor saves its placement. It still renders, so the page's first paint happens.
	/// </summary>
	public bool TestWindow { get; init; }

	/// <summary>The single-instance mutex name for this run.</summary>
	public string MutexName => DataRoot == null
		? Constants.Identity.SingleInstanceMutexName
		: Constants.Identity.SingleInstanceMutexName + "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(DataRoot).ToLowerInvariant())))[..16];

	public static StartupOptions Parse(IReadOnlyList<string> args)
	{
		string? daemonFile = null;
		string? dataRoot = null;
		for (var i = 0; i < args.Count - 1; i++)
		{
			if (string.Equals(args[i], DaemonFileArgument, StringComparison.OrdinalIgnoreCase))
				daemonFile = args[++i];
			else if (string.Equals(args[i], DataRootArgument, StringComparison.OrdinalIgnoreCase))
				dataRoot = args[++i];
		}

		var testWindow = args.Any(a => string.Equals(a, TestWindowArgument, StringComparison.OrdinalIgnoreCase));
		return new StartupOptions { DaemonFile = daemonFile, DataRoot = dataRoot, TestWindow = testWindow };
	}
}
