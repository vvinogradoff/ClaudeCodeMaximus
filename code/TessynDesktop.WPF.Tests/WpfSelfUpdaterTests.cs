using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Switch;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>The WPF self-update decision and its script (W13, decision A23 Q7); the script itself is never run here.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class WpfSelfUpdaterTests : IDisposable
{
	private sealed class RecordingLauncher : IUpdateScriptLauncher
	{
		public List<string> Scripts { get; } = [];

		public bool Throw { get; set; }

		public void Start(string scriptPath)
		{
			if (Throw)
				throw new InvalidOperationException("no powershell");

			Scripts.Add(scriptPath);
		}
	}

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-switch-update-" + Guid.NewGuid().ToString("N"));
	private readonly RecordingLauncher _launcher = new();

	public WpfSelfUpdaterTests()
	{
		Directory.CreateDirectory(Path.Combine(_root, "scripts"));
	}

	public void Dispose()
	{
		TestDirectory.Delete(_root);
	}

	private string ScriptDir => Path.Combine(_root, "scripts");

	private string RunFolder => Path.Combine(_root, "[Publish-WPF]");

	private string MakeAssembly(string dir, DateTime stamp)
	{
		Directory.CreateDirectory(dir);
		var path = Path.Combine(dir, "TessynDesktop.WPF.dll");
		File.WriteAllText(path, "x");
		File.SetLastWriteTimeUtc(path, stamp);
		return dir;
	}

	private string MakeBuild(DateTime stamp) =>
		MakeAssembly(Path.Combine(_root, "src", "code", "TessynDesktop.WPF", "bin", "Debug", "net10.0-windows10.0.17763.0"), stamp);

	private WpfSelfUpdater Create(string runFolder, string? source) => new(runFolder, source, _launcher, ScriptDir, NullLogger.Instance);

	[Fact]
	public void NewerBuild_SchedulesTheCopyAndRestart()
	{
		MakeAssembly(RunFolder, DateTime.UtcNow.AddHours(-2));
		var build = MakeBuild(DateTime.UtcNow);

		var outcome = Create(RunFolder, Path.Combine(_root, "src")).CheckAndScheduleRestart(4242, ["--foo", "bar baz"]);

		Assert.Equal(SelfUpdateOutcome.RestartScheduled, outcome);
		var script = File.ReadAllText(Assert.Single(_launcher.Scripts));
		Assert.Contains($"$source = '{build}'", script);
		Assert.Contains($"$dest = '{RunFolder}'", script);
		Assert.Contains($"$psi.FileName = '{Path.Combine(RunFolder, "TessynDesktop.WPF.exe")}'", script);
		Assert.Contains("Wait-Process -Id 4242", script);
		Assert.Contains("--no-self-update --foo \"bar baz\"", script);
		Assert.DoesNotContain("/MIR", script); // nothing is deleted: the host folder lives in the run folder
	}

	[Fact]
	public void UpToDateRunFolder_StartsNothing()
	{
		var stamp = DateTime.UtcNow.AddMinutes(-5);
		MakeAssembly(RunFolder, stamp);
		MakeBuild(stamp);

		Assert.Equal(SelfUpdateOutcome.UpToDate, Create(RunFolder, Path.Combine(_root, "src")).CheckAndScheduleRestart(1, []));
		Assert.Empty(_launcher.Scripts);
	}

	[Fact]
	public void AnOlderBuildThanTheRunFolder_IsNotCopiedBack()
	{
		MakeAssembly(RunFolder, DateTime.UtcNow);
		MakeBuild(DateTime.UtcNow.AddDays(-1));

		Assert.Equal(SelfUpdateOutcome.UpToDate, Create(RunFolder, Path.Combine(_root, "src")).CheckAndScheduleRestart(1, []));
	}

	[Fact]
	public void RunningFromTheBuildOutput_NeverUpdatesItself()
	{
		var build = MakeBuild(DateTime.UtcNow);

		var outcome = Create(build, Path.Combine(_root, "src")).CheckAndScheduleRestart(1, []);

		Assert.Equal(SelfUpdateOutcome.RunningFromBuildOutput, outcome);
		Assert.Empty(_launcher.Scripts);
	}

	[Fact]
	public void NoSourceLocationOrNoBuild_IsNotAnError()
	{
		MakeAssembly(RunFolder, DateTime.UtcNow);

		Assert.Equal(SelfUpdateOutcome.NoBuild, Create(RunFolder, null).CheckAndScheduleRestart(1, []));
		Assert.Equal(SelfUpdateOutcome.NoBuild, Create(RunFolder, Path.Combine(_root, "empty")).CheckAndScheduleRestart(1, []));
		Assert.Empty(_launcher.Scripts);
	}

	[Fact]
	public void ALauncherThatFails_LeavesTheClientRunningOnTheOldBuild()
	{
		MakeAssembly(RunFolder, DateTime.UtcNow.AddHours(-1));
		MakeBuild(DateTime.UtcNow);
		_launcher.Throw = true;

		Assert.Equal(SelfUpdateOutcome.ScriptFailed, Create(RunFolder, Path.Combine(_root, "src")).CheckAndScheduleRestart(1, []));
	}

	[Fact]
	public void TheRestartArgumentIsNeverDuplicated()
	{
		MakeAssembly(RunFolder, DateTime.UtcNow.AddHours(-1));
		MakeBuild(DateTime.UtcNow);

		Create(RunFolder, Path.Combine(_root, "src")).CheckAndScheduleRestart(1, ["--no-self-update"]);

		var script = File.ReadAllText(Assert.Single(_launcher.Scripts));
		Assert.Single(System.Text.RegularExpressions.Regex.Matches(script, "--no-self-update"));
	}

	private async Task<string> RunScriptAsync(string script)
	{
		var scriptPath = Path.Combine(ScriptDir, "run-" + Guid.NewGuid().ToString("N") + ".ps1");
		File.WriteAllText(scriptPath, script, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		using var powershell = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
		var output = await powershell.StandardOutput.ReadToEndAsync() + await powershell.StandardError.ReadToEndAsync();
		await powershell.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(90)).Token);
		return output;
	}

	private static async Task WaitForFileAsync(string path)
	{
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (!File.Exists(path) && DateTime.UtcNow < deadline)
			await Task.Delay(50);
	}

	private static int ExitedProcessId()
	{
		using var finished = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!;
		finished.WaitForExit();
		return finished.Id;
	}

	[Fact]
	public async Task TheScript_CopiesTheBuildKeepsTheHostFolderAndStartsTheClientAgain_FromTheRealFolderNameWithBrackets()
	{
		// H1: the real script, run by real PowerShell, in a run folder literally named [Publish-WPF] (Start-Process treats that name as an invalid wildcard).
		// cmd.exe stands in for the client and writes a marker into the working directory.
		var source = Path.Combine(_root, "build");
		var run = Path.Combine(_root, "[Publish-WPF]");
		Directory.CreateDirectory(Path.Combine(source, "Web"));
		File.WriteAllText(Path.Combine(source, "TessynDesktop.WPF.dll"), "new build");
		File.WriteAllText(Path.Combine(source, "Web", "page.html"), "page");
		Directory.CreateDirectory(Path.Combine(run, "host"));
		File.WriteAllText(Path.Combine(run, "host", "TessynDesktop.exe"), "host exe");
		File.WriteAllText(Path.Combine(run, "TessynDesktop.WPF.dll"), "old");
		var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
		var result = Path.Combine(ScriptDir, "result.txt");

		var output = await RunScriptAsync(WpfSelfUpdater.BuildScript(source, run, cmd, ["/c", "echo restarted> restarted.txt"], ExitedProcessId(), result, [0]));

		var marker = Path.Combine(run, "restarted.txt");
		await WaitForFileAsync(marker);
		Assert.Equal("new build", File.ReadAllText(Path.Combine(run, "TessynDesktop.WPF.dll")));
		Assert.Equal("page", File.ReadAllText(Path.Combine(run, "Web", "page.html")));
		Assert.Equal("host exe", File.ReadAllText(Path.Combine(run, "host", "TessynDesktop.exe")));
		Assert.True(File.Exists(marker), "the client was not started again from the run folder. Script output: " + output);
		Assert.Equal("ok", File.ReadAllText(result).Trim());
	}

	[Fact]
	public async Task TheScript_AFailedCopyIsRecordedAndTheNextStartWarnsAboutIt()
	{
		// M3: the source is missing, so robocopy exits with 16 after every retry. The script still starts the client, and records the failure.
		var run = Path.Combine(_root, "[Publish-WPF]");
		Directory.CreateDirectory(run);
		var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
		var logger = new TessynDesktop.WPF.Tests.Fakes.CapturingLogger<WpfSelfUpdater>();
		var updater = new WpfSelfUpdater(run, null, _launcher, ScriptDir, logger);

		await RunScriptAsync(WpfSelfUpdater.BuildScript(Path.Combine(_root, "missing"), run, cmd, ["/c", "echo r> restarted.txt"], ExitedProcessId(), updater.ResultPath, [0, 0]));

		await WaitForFileAsync(Path.Combine(run, "restarted.txt"));
		Assert.True(File.Exists(Path.Combine(run, "restarted.txt")));
		Assert.StartsWith("failed: robocopy exit code 16", File.ReadAllText(updater.ResultPath).Trim());

		updater.CheckAndScheduleRestart(1, []);

		Assert.Contains(logger.Messages, m => m.Contains("did not finish copying") && m.Contains("16"));
		Assert.False(File.Exists(updater.ResultPath)); // reported once
	}

	[Fact]
	public async Task TheRestartScript_StartsTheClientFromAFolderWithBrackets()
	{
		var run = Path.Combine(_root, "[Publish-WPF]");
		Directory.CreateDirectory(run);
		var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

		var output = await RunScriptAsync(WpfSelfUpdater.BuildRestartScript(cmd, ["/c", "echo r> restarted.txt"], ExitedProcessId(), run));

		await WaitForFileAsync(Path.Combine(run, "restarted.txt"));
		Assert.True(File.Exists(Path.Combine(run, "restarted.txt")), output);
	}

	[Fact]
	public void TheScript_StartsTheClientWithProcessStart_NotWithStartProcess()
	{
		var script = WpfSelfUpdater.BuildScript(@"C:\b", @"C:\[Publish-WPF]", @"C:\[Publish-WPF]\TessynDesktop.WPF.exe", ["--x"], 1, @"C:\r.txt");

		Assert.Contains("[System.Diagnostics.Process]::Start($psi)", script);
		Assert.DoesNotContain("Start-Process", script);
	}

	[Fact]
	public void TheScriptFileNameCarriesTheProcessId_SoTwoRestartsNeverOverwriteEachOther()
	{
		MakeAssembly(RunFolder, DateTime.UtcNow.AddHours(-1));
		MakeBuild(DateTime.UtcNow);

		Create(RunFolder, Path.Combine(_root, "src")).CheckAndScheduleRestart(4711, []);

		Assert.Contains("_4711.ps1", Assert.Single(_launcher.Scripts));
	}

	[Fact]
	public void NeedsUpdate_ARunFolderMissingAFileOfTheBuildIsStale_EvenWhenTheMainAssemblyIsCurrent()
	{
		// M3: a copy that stopped half way left the main assembly current and another file behind.
		var stamp = DateTime.UtcNow.AddMinutes(-5);
		var build = MakeBuild(stamp);
		File.WriteAllText(Path.Combine(build, "Other.dll"), "o");
		File.SetLastWriteTimeUtc(Path.Combine(build, "Other.dll"), stamp);
		MakeAssembly(RunFolder, stamp);

		Assert.True(WpfSelfUpdater.NeedsUpdate(build, RunFolder));
	}

	[Fact]
	public void NeedsUpdate_AnyNewerFileInTheBuildMakesItStale_NotOnlyTheMainAssembly()
	{
		var stamp = DateTime.UtcNow.AddHours(-1);
		var build = MakeBuild(stamp);
		File.WriteAllText(Path.Combine(build, "Other.dll"), "o");
		File.SetLastWriteTimeUtc(Path.Combine(build, "Other.dll"), DateTime.UtcNow);
		MakeAssembly(RunFolder, stamp);
		File.WriteAllText(Path.Combine(RunFolder, "Other.dll"), "o");
		File.SetLastWriteTimeUtc(Path.Combine(RunFolder, "Other.dll"), stamp);

		Assert.True(WpfSelfUpdater.NeedsUpdate(build, RunFolder));
	}

	[Fact]
	public void NeedsUpdate_ACompleteCopyIsCurrent_AndANewerHostFolderDoesNotHideAStaleClient()
	{
		var stamp = DateTime.UtcNow.AddHours(-1);
		var build = MakeBuild(stamp);
		MakeAssembly(RunFolder, stamp);
		Assert.False(WpfSelfUpdater.NeedsUpdate(build, RunFolder));

		// A freshly copied host file inside the run folder must not make an old client look current.
		File.SetLastWriteTimeUtc(Path.Combine(RunFolder, "TessynDesktop.WPF.dll"), stamp.AddHours(-2));
		Directory.CreateDirectory(Path.Combine(RunFolder, "host"));
		File.WriteAllText(Path.Combine(RunFolder, "host", "TessynDesktop.dll"), "h");
		Assert.True(WpfSelfUpdater.NeedsUpdate(build, RunFolder));
	}

	[Fact]
	public void BuildScript_EscapesApostrophesInPaths()
	{
		var script = WpfSelfUpdater.BuildScript(@"C:\o'brien\build", @"C:\run folder", @"C:\run folder\TessynDesktop.WPF.exe", ["--no-self-update"], 7, @"C:\r.txt");

		Assert.Contains(@"$source = 'C:\o''brien\build'", script);
	}

	[Theory]
	[InlineData(false, false, null, null, true, true)]
	[InlineData(true, false, null, null, false, true)]
	[InlineData(false, true, null, null, true, false)]
	[InlineData(false, false, @"C:\tmp\root", null, false, false)]
	[InlineData(false, false, null, @"C:\tmp\daemon.json", false, false)]
	public void StartupOptions_SwitchTheSelfUpdateAndTheHostStartOffInACustomEnvironment(bool noUpdate, bool noHost, string? dataRoot, string? daemonFile, bool updateOn, bool hostOn)
	{
		var args = new List<string>();
		if (noUpdate)
			args.Add("--no-self-update");
		if (noHost)
			args.Add("--no-host-start");
		if (dataRoot != null)
			args.AddRange(["--data-root", dataRoot]);
		if (daemonFile != null)
			args.AddRange(["--daemon-file", daemonFile]);

		var options = StartupOptions.Parse(args);

		Assert.Equal(updateOn, options.SelfUpdateEnabled);
		Assert.Equal(hostOn, options.AutoStartHostEnabled);
	}

	[Theory]
	[InlineData(new string[0], true)]
	[InlineData(new[] { "--test-window" }, false)]
	[InlineData(new[] { "--data-root", @"C:	mpoot" }, false)]
	[InlineData(new[] { "--daemon-file", @"C:	mp\daemon.json" }, false)]
	public void StartupOptions_TheRealEnvironmentIsTheOnlyOneThatImportsUpdatesAndStartsAHost(string[] args, bool real)
	{
		// L2: the import dialog is registered only when IsRealEnvironment (ServiceRegistration), like the update and the host start.
		Assert.Equal(real, StartupOptions.Parse(args).IsRealEnvironment);
	}

	[Fact]
	public void StartupOptions_ATestWindowNeverUpdatesOrStartsAHost()
	{
		var options = StartupOptions.Parse(["--test-window"]);

		Assert.False(options.SelfUpdateEnabled);
		Assert.False(options.AutoStartHostEnabled);
	}
}
