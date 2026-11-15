namespace TessynDesktop.WPF;

public static partial class Constants
{
	/// <summary>The run folder, the Avalonia background host and the self-update (A23 Q6, Q7).</summary>
	public static class Switch
	{
		/// <summary>The WPF run folder next to <c>code\</c>; the build output is never run from (rule A15).</summary>
		public const string RunFolderName = "[Publish-WPF]";

		/// <summary>The Avalonia daemon host's folder inside the run folder.</summary>
		public const string HostFolderName = "host";

		public const string HostExeName = "TessynDesktop.exe";
		public const string HostAssemblyName = "TessynDesktop.dll";
		public const string WpfExeName = "TessynDesktop.WPF.exe";
		public const string WpfAssemblyName = "TessynDesktop.WPF.dll";

		/// <summary>Project folders under <c>code\</c>.</summary>
		public const string WpfProjectFolder = "TessynDesktop.WPF";
		public const string HostProjectFolder = "TessynDesktop";
		public const string CodeFolder = "code";

		public const string AppSettingsFileName = "appsettings.json";
		public const string SourceCodesLocationProperty = "SourceCodesLocation";

		/// <summary>The Avalonia flag that hosts the daemon without a window.</summary>
		public const string BackgroundArgument = "--background";

		/// <summary>The restarted WPF process must not look for an update again (a copy that keeps failing would otherwise restart forever).</summary>
		public const string NoSelfUpdateArgument = "--no-self-update";

		/// <summary>Never start a daemon host (a custom environment such as a test).</summary>
		public const string NoHostStartArgument = "--no-host-start";

		/// <summary>The updater script's file name in the temp folder.</summary>
		public const string UpdateScriptFileFormat = "TessynDesktop_WPF_update_{0}.ps1";

		/// <summary>Where the update script records "ok" or "failed ..." for the next start.</summary>
		public const string UpdateResultFileName = "TessynDesktop_WPF_update.result.txt";

		/// <summary>The shortest time between two host start attempts of one WPF process.</summary>
		public static readonly TimeSpan HostStartCooldown = TimeSpan.FromSeconds(60);

		/// <summary>How long the updater script waits for this process to exit.</summary>
		public const int ScriptWaitForExitSeconds = 30;

		public static readonly int[] ScriptRetryDelaysSeconds = [1, 2, 4, 8, 16];

		public const string RestartScriptFileFormat = "TessynDesktop_WPF_restart_{0}.ps1";

		public const string RestartTitle = "Restart the Tessyn daemon";

		public const string RestartConfirm = "Restart daemon";

		/// <summary>The banner when the daemon runs an older build than the newest host build.</summary>
		public const string DaemonOlderText = "The daemon is older than the build.";

		/// <summary>How long the client waits for the host it started to publish daemon.json before it says so.</summary>
		public static readonly TimeSpan StartingTimeout = TimeSpan.FromSeconds(45);

		public static readonly TimeSpan DaemonExitTimeout = TimeSpan.FromSeconds(40);

		public const string StartingTimedOutDetail = "the host started but did not publish daemon.json; are the agent tools switched off in the Avalonia settings?";

		public const string TokenUnavailableDetail = "daemon.json is there but its token cannot be read (another Windows user?)";

		public const string ImportsSection = "imports";

		public const string ImportTitle = "Import Avalonia settings";

		public const string ImportMessage =
			"Import your Avalonia settings (theme, font sizes, key bindings and screensaver) into this client? This asks only once.";

		public const string ImportConfirm = "Import";

		public const string ImportSkip = "Not now";

		/// <summary>Avalonia key actions that have a WPF command, by Avalonia action name.</summary>
		public static readonly IReadOnlyDictionary<string, string> KeyActionToCommand = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["AddDirectory"] = TreeCommands.AddDirectory,
		};
	}
}
