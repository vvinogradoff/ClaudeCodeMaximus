using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Switch;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The self-update while the client runs and on exit: the once-a-minute check, the notice with "Restart now", and the copy on exit that never restarts.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class LiveUpdateServiceTests : IDisposable
{
	private sealed class RecordingLauncher : IUpdateScriptLauncher
	{
		public List<string> Scripts { get; } = [];

		public void Start(string scriptPath) => Scripts.Add(scriptPath);
	}

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-live-update-" + Guid.NewGuid().ToString("N"));
	private readonly RecordingLauncher _launcher = new();
	private readonly UpdateNoticeViewModel _notice = new();
	private int _shutdowns;

	public LiveUpdateServiceTests()
	{
		Directory.CreateDirectory(Path.Combine(_root, "scripts"));
	}

	public void Dispose() => TestDirectory.Delete(_root);

	private string RunFolder => Path.Combine(_root, "[Publish-WPF]");

	private void Assembly(string dir, DateTime stamp)
	{
		Directory.CreateDirectory(dir);
		var path = Path.Combine(dir, "TessynDesktop.WPF.dll");
		File.WriteAllText(path, "x");
		File.SetLastWriteTimeUtc(path, stamp);
	}

	private void Build(DateTime stamp) => Assembly(Path.Combine(_root, "src", "code", "TessynDesktop.WPF", "bin", "Debug", "net10.0-windows10.0.17763.0"), stamp);

	private LiveUpdateService Create() => new(
		new WpfSelfUpdater(RunFolder, Path.Combine(_root, "src"), _launcher, Path.Combine(_root, "scripts"), NullLogger.Instance), _notice, new ImmediateUiDispatcher(),
		["--foo"], () => _shutdowns++, TimeSpan.FromMinutes(1), 4711, NullLogger.Instance);

	[Fact]
	public void TheNoticeIsHiddenUntilANewerBuildAppears_ThenShownWithItsText_AndHiddenAgainWhenInstalled()
	{
		var old = DateTime.UtcNow.AddHours(-1);
		Assembly(RunFolder, old);
		Build(old);
		using var service = Create();

		Assert.False(service.CheckNow());
		Assert.False(_notice.IsVisible);

		Build(DateTime.UtcNow);
		Assert.True(service.CheckNow());
		Assert.True(_notice.IsVisible);
		Assert.Equal("A newer build is ready — it is installed when you close Tessyn.", _notice.Text);
		Assert.Equal("Restart now", _notice.RestartNowText);

		Assembly(RunFolder, DateTime.UtcNow.AddMinutes(1)); // the copy happened
		Assert.False(service.CheckNow());
		Assert.False(_notice.IsVisible);
	}

	[Fact]
	public void RestartNow_RunsTheStartupUpdate_AndShutsTheClientDown()
	{
		Assembly(RunFolder, DateTime.UtcNow.AddHours(-1));
		Build(DateTime.UtcNow);
		using var service = Create();

		_notice.RestartNowCommand.Execute(null);

		var script = File.ReadAllText(Assert.Single(_launcher.Scripts));
		Assert.Contains("[System.Diagnostics.Process]::Start($psi)", script); // this one restarts the client
		Assert.Contains("--no-self-update --foo", script);
		Assert.Equal(1, _shutdowns);
	}

	[Fact]
	public void OnExit_ANewerBuildIsCopiedWithoutRestarting()
	{
		Assembly(RunFolder, DateTime.UtcNow.AddHours(-1));
		Build(DateTime.UtcNow);
		using var service = Create();

		Assert.Equal(SelfUpdateOutcome.CopyScheduledOnExit, service.OnExit());

		var script = File.ReadAllText(Assert.Single(_launcher.Scripts));
		Assert.DoesNotContain("Process]::Start", script);
		Assert.Equal(0, _shutdowns);
	}

	[Fact]
	public void OnExit_NothingNewer_StartsNoScript()
	{
		var stamp = DateTime.UtcNow.AddHours(-1);
		Assembly(RunFolder, stamp);
		Build(stamp);
		using var service = Create();

		Assert.Equal(SelfUpdateOutcome.NothingToCopy, service.OnExit());
		Assert.Empty(_launcher.Scripts);
	}

	[Fact]
	public void OnExit_AfterARestartScriptWasStarted_NoSecondScriptRunsBesideIt()
	{
		Assembly(RunFolder, DateTime.UtcNow.AddHours(-1));
		Build(DateTime.UtcNow);
		using var service = Create();

		service.MarkRestarting(); // the daemon restart (or "Restart now") already has a script waiting for this process

		Assert.Equal(SelfUpdateOutcome.Disabled, service.OnExit());
		Assert.Empty(_launcher.Scripts);
	}

	[Fact]
	public async Task TheLoopChecksPeriodically()
	{
		Assembly(RunFolder, DateTime.UtcNow.AddHours(-1));
		Build(DateTime.UtcNow);
		using var service = new LiveUpdateService(
			new WpfSelfUpdater(RunFolder, Path.Combine(_root, "src"), _launcher, Path.Combine(_root, "scripts"), NullLogger.Instance), _notice, new ImmediateUiDispatcher(),
			[], () => { }, TimeSpan.FromMilliseconds(30), 1, NullLogger.Instance);

		service.Start();
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!_notice.IsVisible && DateTime.UtcNow < deadline)
			await Task.Delay(20);

		Assert.True(_notice.IsVisible);
		service.OnExit(); // stops the loop
	}

	[Fact]
	public void TheLiveUpdateIsOnlyForTheRealEnvironment_NotATestWindowCustomDataRootOrDaemonFile()
	{
		// ServiceRegistration registers LiveUpdateService only when options.IsRealEnvironment (and the run folder is no build output); the exit and the timer exist only then.
		Assert.True(Infrastructure.StartupOptions.Parse([]).IsRealEnvironment);
		Assert.False(Infrastructure.StartupOptions.Parse(["--test-window"]).IsRealEnvironment);
		Assert.False(Infrastructure.StartupOptions.Parse(["--data-root", Path.Combine(_root, "dr")]).IsRealEnvironment);
		Assert.False(Infrastructure.StartupOptions.Parse(["--daemon-file", Path.Combine(_root, "d.json")]).IsRealEnvironment);
	}
}
