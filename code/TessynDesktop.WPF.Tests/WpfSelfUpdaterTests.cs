using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Switch;

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
		try
		{
			Directory.Delete(_root, recursive: true);
		}
		catch (IOException)
		{
		}
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
		Assert.Contains($"$exe = '{Path.Combine(RunFolder, "TessynDesktop.WPF.exe")}'", script);
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

	[Fact]
	public async Task TheScript_CopiesTheBuildKeepsTheHostFolderAndStartsTheClientAgain()
	{
		// The real script, run by real PowerShell on temp folders; cmd.exe stands in for the client and writes a marker into the working directory.
		var source = Path.Combine(_root, "build");
		var run = Path.Combine(_root, "run folder");
		Directory.CreateDirectory(Path.Combine(source, "Web"));
		File.WriteAllText(Path.Combine(source, "TessynDesktop.WPF.dll"), "new build");
		File.WriteAllText(Path.Combine(source, "Web", "page.html"), "page");
		Directory.CreateDirectory(Path.Combine(run, "host"));
		File.WriteAllText(Path.Combine(run, "host", "TessynDesktop.exe"), "host exe");
		File.WriteAllText(Path.Combine(run, "TessynDesktop.WPF.dll"), "old");

		using var finished = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!;
		finished.WaitForExit();
		var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
		var scriptPath = Path.Combine(ScriptDir, "run-update.ps1");
		File.WriteAllText(scriptPath, WpfSelfUpdater.BuildScript(source, run, cmd, ["/c", "echo restarted> restarted.txt"], finished.Id));

		using var powershell = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"") { CreateNoWindow = true, UseShellExecute = false })!;
		await powershell.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(90)).Token);

		var marker = Path.Combine(run, "restarted.txt");
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (!File.Exists(marker) && DateTime.UtcNow < deadline)
			await Task.Delay(50);

		Assert.Equal("new build", File.ReadAllText(Path.Combine(run, "TessynDesktop.WPF.dll")));
		Assert.Equal("page", File.ReadAllText(Path.Combine(run, "Web", "page.html")));
		Assert.Equal("host exe", File.ReadAllText(Path.Combine(run, "host", "TessynDesktop.exe")));
		Assert.True(File.Exists(marker), "the client was not started again from the run folder");
	}

	[Fact]
	public void BuildScript_EscapesApostrophesInPaths()
	{
		var script = WpfSelfUpdater.BuildScript(@"C:\o'brien\build", @"C:\run folder", @"C:\run folder\TessynDesktop.WPF.exe", ["--no-self-update"], 7);

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

	[Fact]
	public void StartupOptions_ATestWindowNeverUpdatesOrStartsAHost()
	{
		var options = StartupOptions.Parse(["--test-window"]);

		Assert.False(options.SelfUpdateEnabled);
		Assert.False(options.AutoStartHostEnabled);
	}
}
