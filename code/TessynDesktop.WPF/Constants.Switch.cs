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

		/// <summary>Every assembly of the daemon host (the exe assembly, Core, Protocol): a build may change only some of them.</summary>
		public const string HostAssemblyPattern = "TessynDesktop*.dll";
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

		/// <summary>The copy-on-exit script's file name in the temp folder (it starts nothing).</summary>
		public const string ExitUpdateScriptFileFormat = "TessynDesktop_WPF_exit_update_{0}.ps1";

		/// <summary>How often the running client looks for a newer build.</summary>
		public static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromSeconds(60);

		/// <summary>The notice while a newer build is ready (the "Restart now" link follows it).</summary>
		public const string UpdateReadyText = "A newer build is ready — it is installed when you close Tessyn.";

		public const string RestartNowText = "Restart now";

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

		/// <summary>FR.32.10: the shell verb that starts a program through the Windows administrator prompt.</summary>
		public const string RunAsVerb = "runas";

		/// <summary>FR.32.13: the Win32 error of a declined administrator prompt (ERROR_CANCELLED).</summary>
		public const int ElevationCancelledError = 1223;

		/// <summary>FR.32.12: the banner while "Start daemon elevated" is on and the daemon runs normally.</summary>
		public const string ElevationExpectedText = "The daemon runs normally, but Start daemon elevated is on.";

		/// <summary>FR.32.13: the notices after a declined or failed elevated start ({0} is the reason).</summary>
		public const string ElevationDeclinedText = "The daemon runs normally: the administrator prompt was declined.";

		public const string ElevationFailedFormat = "The daemon runs normally: the elevated start failed ({0}).";

		public const string ElevationUnknownReason = "the process did not start";

		/// <summary>FR.32.11: the restart buttons and their confirmation.</summary>
		public const string RestartElevatedTitle = "Restart the daemon elevated";

		public const string RestartNormalTitle = "Restart the daemon normally";

		public const string RestartElevatedConfirm = "Restart daemon elevated";

		public const string RestartNormalConfirm = "Restart daemon normal";

		/// <summary>FR.32.11: why a restart did not bring a new daemon up.</summary>
		public const string RestartNotSupportedNote = "this daemon cannot be restarted from here; exit it from its tray icon or window, then start the client";

		public const string RestartDidNotExitNote = "the daemon did not exit in time; exit it from its tray icon";

		public const string RestartNotStartedNote = "the daemon ended, but a new one did not start; start Tessyn again";

		/// <summary>The banner while no daemon runs and starting one is switched off (side-by-side with the Avalonia app).</summary>
		public const string WaitingText = "Waiting for Tessyn to start…";

		/// <summary>How long the client waits for the host it started to publish daemon.json before it says so.</summary>
		public static readonly TimeSpan StartingTimeout = TimeSpan.FromSeconds(45);

		public static readonly TimeSpan DaemonExitTimeout = TimeSpan.FromSeconds(40);

		/// <summary>FR.32.9: the git executable, and the longest the whole stale-daemon comparison may take before the time rule of FR.32.7 decides.</summary>
		public const string GitExecutable = "git";

		public static readonly TimeSpan GitCheckTimeout = TimeSpan.FromSeconds(5);

		/// <summary>FR.32.9: the project file extension followed to find the daemon's source paths, and the element naming a reference.</summary>
		public const string ProjectFileExtension = ".csproj";

		public const string ProjectReferenceElement = "ProjectReference";

		public const string IncludeAttribute = "Include";

		/// <summary>FR.32.9: the build files under <c>code\</c> that affect the daemon's assemblies (git pathspec patterns).</summary>
		public static readonly string[] DaemonBuildFilePatterns = ["Directory.Build.*", "Directory.Packages.props", "global.json", "NuGet.config"];

		/// <summary>FR.32.9: the assembly attribute holding <c>1.0.0+&lt;commit&gt;</c>, and the character that separates the version from the commit.</summary>
		public const string InformationalVersionAttributeName = "AssemblyInformationalVersionAttribute";

		public const string ReflectionNamespace = "System.Reflection";

		public const char CommitSeparator = '+';

		/// <summary>FR.32.9: the shortest hex string accepted as a commit.</summary>
		public const int MinimumCommitLength = 7;

		public const string StartingTimedOutDetail = "the host started but did not publish daemon.json; are the agent tools switched off in the settings?";

		public const string TokenUnavailableDetail = "daemon.json is there but its token cannot be read (another Windows user?)";
	}
}
