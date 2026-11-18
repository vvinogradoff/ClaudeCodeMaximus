using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.WPF.Switch;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.32.9 / UC.HS.07 / TC.HS.16-22: the stale-daemon banner only when the daemon's source changed, decided by git over the daemon's source paths.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonStaleCheckTests : IDisposable
{
	private sealed class MapCommitReader : IDaemonCommitReader
	{
		public Dictionary<string, string?> Commits { get; } = new(StringComparer.OrdinalIgnoreCase);

		public string? ReadCommit(string folder) => Commits.GetValueOrDefault(Path.GetFullPath(folder));
	}

	private sealed class FakeGit(GitResult result) : IGitRunner
	{
		public List<string> Calls { get; } = [];

		public GitResult Run(string workingDirectory, IReadOnlyList<string> arguments, TimeSpan timeout)
		{
			Calls.Add(string.Join(' ', arguments));
			return result;
		}
	}

	private sealed class FakeDetector(Func<DaemonChangeResult> evaluate) : IDaemonChangeDetector
	{
		public int Evaluations;

		public DaemonChangeResult Evaluate(string runningFolder, string buildFolder, string sourceLocation)
		{
			Interlocked.Increment(ref Evaluations);
			return evaluate();
		}
	}

	private sealed class FakeProbe : IDaemonBuildProbe
	{
		public DateTime Running { get; set; }

		public string Folder { get; set; } = "";

		public RunningDaemonBuild? Inspect(int pid) => new(Folder, Running);
	}

	private static readonly DateTime Build = new(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);
	private static readonly string MissingCommit = new('a', 40);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-stale-" + Guid.NewGuid().ToString("N"));
	private readonly string _source;
	private readonly string _running;
	private readonly string _buildFolder;
	private readonly MapCommitReader _commits = new();
	private readonly FakeProbe _probe = new();

	public DaemonStaleCheckTests()
	{
		_source = Path.Combine(_root, "src");
		_running = Path.Combine(_root, "running");
		_buildFolder = Path.Combine(_source, "code", "TessynDesktop", "bin", "Debug", "net10.0-windows10.0.17763.0");
		Directory.CreateDirectory(_running);
		Directory.CreateDirectory(_buildFolder);
		File.WriteAllText(Path.Combine(_buildFolder, "TessynDesktop.dll"), "dll");
		File.SetLastWriteTimeUtc(Path.Combine(_buildFolder, "TessynDesktop.dll"), Build);
		_probe.Folder = _running;
		_probe.Running = Build.AddHours(-3); // the time rule alone would show the banner
	}

	public void Dispose()
	{
		if (Directory.Exists(_root))
		{
			foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
				File.SetAttributes(file, FileAttributes.Normal); // git's object files are read-only
		}

		TestDirectory.Delete(_root);
	}

	// ---- a real temporary git repository laid out like the product ----

	private string Git(params string[] arguments)
	{
		var info = new ProcessStartInfo("git") { WorkingDirectory = _source, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
		foreach (var argument in new[] { "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false" }.Concat(arguments))
			info.ArgumentList.Add(argument);

		using var process = Process.Start(info)!;
		var output = process.StandardOutput.ReadToEnd();
		var error = process.StandardError.ReadToEnd();
		process.WaitForExit();
		Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
		return output.Trim();
	}

	private void Write(string relative, string content)
	{
		var path = Path.Combine(_source, relative.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content);
	}

	private static string Project(params string[] references) =>
		"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n" + string.Concat(references.Select(r => $"    <ProjectReference Include=\"..\\{r}\\{r}.csproj\" />\n")) + "  </ItemGroup>\n</Project>\n";

	/// <summary>The product's layout, committed; returns the first commit.</summary>
	private string InitRepository()
	{
		Git("init", "-q", "-b", "main");
		Write(".gitignore", "bin/\nobj/\n");
		Write("code/TessynDesktop/TessynDesktop.csproj", Project("TessynDesktop.Core"));
		Write("code/TessynDesktop/Program.cs", "// host");
		Write("code/TessynDesktop.Core/TessynDesktop.Core.csproj", Project("TessynDesktop.Protocol"));
		Write("code/TessynDesktop.Core/Core.cs", "// core");
		Write("code/TessynDesktop.Protocol/TessynDesktop.Protocol.csproj", Project());
		Write("code/TessynDesktop.Protocol/Protocol.cs", "// protocol");
		Write("code/TessynDesktop.WPF/TessynDesktop.WPF.csproj", Project("TessynDesktop.Core"));
		Write("code/TessynDesktop.WPF/Window.cs", "// wpf");
		Write("code/Directory.Build.props", "<Project />");
		Write("docs/notes.md", "docs");
		return Commit("initial");
	}

	private string Commit(string message)
	{
		Git("add", "-A");
		Git("commit", "-q", "-m", message);
		return Git("rev-parse", "HEAD");
	}

	private string CommitChange(string relative, string content)
	{
		Write(relative, content);
		return Commit("change " + relative);
	}

	private DaemonChangeDetector Detector(IGitRunner? git = null, TimeSpan? timeout = null) =>
		new(_commits, git ?? new ProcessGitRunner(), new DaemonSourcePathResolver(), timeout ?? TimeSpan.FromSeconds(30));

	private DaemonChangeResult Evaluate(string runningCommit, string newestCommit, IGitRunner? git = null)
	{
		_commits.Commits[Path.GetFullPath(_running)] = runningCommit;
		_commits.Commits[Path.GetFullPath(_buildFolder)] = newestCommit;
		return Detector(git).Evaluate(_running, _buildFolder, _source);
	}

	private DaemonRestarter Restarter(IDaemonChangeDetector? detector, ILogger? logger = null) =>
		new(_source, _probe, null!, null!, null!, null!, TimeSpan.FromSeconds(1), logger ?? NullLogger.Instance, changeDetector: detector);

	// ---- TC.HS.16 ----

	[Fact]
	public void TC_HS_16_TheSameCommitWithACleanTree_ShowsNoBanner_AndRunsNoDiff()
	{
		Write("code/TessynDesktop/TessynDesktop.csproj", Project());
		var git = new FakeGit(new GitResult(true, false, 0, ""));

		var result = Evaluate("1111111", "1111111", git);

		Assert.Equal(DaemonChangeDecision.Unchanged, result.Decision);
		var call = Assert.Single(git.Calls);
		Assert.Contains("status --porcelain", call);
		Assert.Null(Restarter(Detector(git)).Check(1)); // although the time rule alone would say stale
	}

	[Fact]
	public void TC_HS_16_TheSameCommitWithUncommittedDaemonChanges_LeavesTheDecisionToTheTimeRule()
	{
		var commit = InitRepository();
		Write("code/TessynDesktop.Core/Core.cs", "// uncommitted edit");
		_commits.Commits[Path.GetFullPath(_running)] = commit;
		_commits.Commits[Path.GetFullPath(_buildFolder)] = commit;

		Assert.Equal(DaemonChangeDecision.Unknown, Detector().Evaluate(_running, _buildFolder, _source).Decision);
		Assert.NotNull(Restarter(Detector()).Check(1)); // stale by time
		_probe.Running = Build;
		Assert.Null(Restarter(Detector()).Check(1)); // current by time
	}

	[Fact]
	public void TC_HS_16_TheSameCommitWithUncommittedChangesOnlyOutsideTheDaemon_ShowsNoBanner()
	{
		var commit = InitRepository();
		Write("code/TessynDesktop.WPF/Window.cs", "// uncommitted wpf edit");

		Assert.Equal(DaemonChangeDecision.Unchanged, Evaluate(commit, commit).Decision);
	}

	[Fact]
	public void TC_HS_16_TheSameCommitWhenGitFails_LeavesTheDecisionToTheTimeRule()
	{
		Write("code/TessynDesktop/TessynDesktop.csproj", Project());

		Assert.Equal(DaemonChangeDecision.Unknown, Evaluate("1111111", "1111111", new FakeGit(GitResult.NotAvailable)).Decision);
		Assert.Equal(DaemonChangeDecision.Unknown, Evaluate("1111111", "1111111", new FakeGit(new GitResult(true, false, 128, ""))).Decision);
	}

	// ---- TC.HS.17 ----

	[Fact]
	public void TC_HS_17_DifferentCommitsWithoutAChangeInTheDaemonSource_ShowNoBanner()
	{
		var first = InitRepository();
		CommitChange("code/TessynDesktop.WPF/Window.cs", "// wpf changed");
		var last = CommitChange("docs/notes.md", "docs changed");
		_commits.Commits[Path.GetFullPath(_running)] = first;
		_commits.Commits[Path.GetFullPath(_buildFolder)] = last;

		var result = Detector().Evaluate(_running, _buildFolder, _source);

		Assert.Equal(DaemonChangeDecision.Unchanged, result.Decision);
		Assert.Equal(first, result.RunningCommit);
		Assert.Equal(last, result.NewestCommit);
		Assert.Null(Restarter(Detector()).Check(1));
	}

	// ---- TC.HS.18 ----

	[Theory]
	[InlineData("code/TessynDesktop.Core/Core.cs")]
	[InlineData("code/TessynDesktop.Protocol/Protocol.cs")]
	[InlineData("code/TessynDesktop/Program.cs")]
	[InlineData("code/Directory.Build.props")]
	[InlineData("code/Directory.Packages.props")]
	[InlineData("code/global.json")]
	[InlineData("code/NuGet.config")]
	public void TC_HS_18_AChangeInTheDaemonSourceBetweenTheCommits_ShowsTheBanner(string changed)
	{
		var first = InitRepository();
		CommitChange("docs/notes.md", "docs changed");
		var last = CommitChange(changed, "changed " + Guid.NewGuid());
		_commits.Commits[Path.GetFullPath(_running)] = first;
		_commits.Commits[Path.GetFullPath(_buildFolder)] = last;
		_probe.Running = Build; // the time rule alone would show nothing

		var stale = Restarter(Detector()).Check(1);

		Assert.NotNull(stale);
		Assert.Equal(Build, stale!.NewestBuildUtc);
	}

	// ---- TC.HS.19 ----

	[Fact]
	public void TC_HS_19_UncommittedChangesInADaemonSourcePath_ShowTheBanner()
	{
		var first = InitRepository();
		var last = CommitChange("docs/notes.md", "docs changed");
		Write("code/TessynDesktop.Core/Core.cs", "// uncommitted edit");

		Assert.Equal(DaemonChangeDecision.Changed, Evaluate(first, last).Decision);
	}

	[Fact]
	public void TC_HS_19_ANewUntrackedFileInADaemonSourcePath_ShowsTheBanner_ButABuildOutputDoesNot()
	{
		var first = InitRepository();
		var last = CommitChange("docs/notes.md", "docs changed");
		Assert.Equal(DaemonChangeDecision.Unchanged, Evaluate(first, last).Decision); // the ignored bin\ folder beside the sources is no change

		Write("code/TessynDesktop.Protocol/NewType.cs", "// new");

		Assert.Equal(DaemonChangeDecision.Changed, Evaluate(first, last).Decision);
	}

	[Fact]
	public void TC_HS_19_UncommittedChangesOutsideTheDaemonSource_AreNotChanges()
	{
		var first = InitRepository();
		var last = CommitChange("docs/notes.md", "docs changed");
		Write("code/TessynDesktop.WPF/Window.cs", "// uncommitted wpf edit");
		Write("docs/draft.md", "draft");

		Assert.Equal(DaemonChangeDecision.Unchanged, Evaluate(first, last).Decision);
	}

	// ---- TC.HS.20 ----

	[Fact]
	public void TC_HS_20_TheSourcePathsFollowTheProjectReferencesTransitively_AndNotAList()
	{
		Write("code/TessynDesktop/TessynDesktop.csproj", Project("Alpha", "Beta"));
		Write("code/Alpha/Alpha.csproj", Project("Gamma", "Beta"));
		Write("code/Beta/Beta.csproj", Project("Gamma"));
		Write("code/Gamma/Gamma.csproj", Project("Alpha")); // a cycle must not hang
		Write("code/Unrelated/Unrelated.csproj", Project());

		var paths = new DaemonSourcePathResolver().Resolve(_source);

		Assert.NotNull(paths);
		Assert.Equal(["code/TessynDesktop", "code/Alpha", "code/Gamma", "code/Beta"], paths!.Take(4));
		Assert.DoesNotContain("code/Unrelated", paths);
		foreach (var file in new[] { "Directory.Build.*", "Directory.Packages.props", "global.json", "NuGet.config" })
			Assert.Contains("code/" + file, paths);
	}

	[Fact]
	public void TC_HS_20_ASourceLocationThatIsTheCodeFolderItselfIsHandled()
	{
		Write("code/TessynDesktop/TessynDesktop.csproj", Project("TessynDesktop.Core"));
		Write("code/TessynDesktop.Core/TessynDesktop.Core.csproj", Project());

		var paths = new DaemonSourcePathResolver().Resolve(Path.Combine(_source, "code"));

		Assert.Equal(["TessynDesktop", "TessynDesktop.Core", "Directory.Build.*", "Directory.Packages.props", "global.json", "NuGet.config"], paths);
	}

	[Fact]
	public void TC_HS_20_AMissingHostOrReferencedProject_GivesNoPaths()
	{
		Assert.Null(new DaemonSourcePathResolver().Resolve(_source));

		Write("code/TessynDesktop/TessynDesktop.csproj", Project("Gone"));
		Assert.Null(new DaemonSourcePathResolver().Resolve(_source));
	}

	[Fact]
	public void TC_HS_20_TheRealHostProject_ResolvesToTheHostCoreAndProtocol()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null && !File.Exists(Path.Combine(directory.FullName, "code", "TessynDesktop", "TessynDesktop.csproj")))
			directory = directory.Parent;
		Assert.NotNull(directory);

		var paths = new DaemonSourcePathResolver().Resolve(directory!.FullName);

		Assert.NotNull(paths);
		Assert.Contains("code/TessynDesktop", paths!);
		Assert.Contains("code/TessynDesktop.Core", paths);
		Assert.Contains("code/TessynDesktop.Protocol", paths);
		Assert.DoesNotContain("code/TessynDesktop.WPF", paths);
	}

	// ---- TC.HS.21 ----

	[Fact]
	public void TC_HS_21_AnUnknownCommit_LeavesTheDecisionToTheTimeRule()
	{
		_commits.Commits[Path.GetFullPath(_buildFolder)] = "2222222"; // the running daemon's commit stays unknown
		var detector = new FakeGit(new GitResult(true, false, 0, ""));

		var result = Detector(detector).Evaluate(_running, _buildFolder, _source);

		Assert.Equal(DaemonChangeDecision.Unknown, result.Decision);
		Assert.Empty(detector.Calls);
		Assert.NotNull(Restarter(Detector(detector)).Check(1)); // stale by time
		_probe.Running = Build;
		Assert.Null(Restarter(Detector(detector)).Check(1)); // current by time
	}

	[Fact]
	public void TC_HS_21_NoGit_AGitTimeout_AndAGitFailure_LeaveTheDecisionToTheTimeRule()
	{
		InitRepository();
		foreach (var failing in new[] { GitResult.NotAvailable, GitResult.OutOfTime, new GitResult(true, false, 128, "") })
		{
			var git = new FakeGit(failing);
			_commits.Commits[Path.GetFullPath(_running)] = "1111111";
			_commits.Commits[Path.GetFullPath(_buildFolder)] = "2222222";
			_probe.Running = Build.AddHours(-3);

			Assert.Equal(DaemonChangeDecision.Unknown, Detector(git).Evaluate(_running, _buildFolder, _source).Decision);
			Assert.NotNull(Restarter(Detector(git)).Check(1));
			_probe.Running = Build;
			Assert.Null(Restarter(Detector(git)).Check(1));
		}
	}

	[Fact]
	public void TC_HS_21_AGitStatusFailureAfterACleanDiff_IsUnknown()
	{
		var diffClean = new SequenceGit(new GitResult(true, false, 0, ""), new GitResult(true, false, 128, ""));
		Write("code/TessynDesktop/TessynDesktop.csproj", Project());

		Assert.Equal(DaemonChangeDecision.Unknown, Evaluate("1111111", "2222222", diffClean).Decision);
		Assert.Equal(2, diffClean.Calls.Count);
	}

	private sealed class SequenceGit(params GitResult[] results) : IGitRunner
	{
		public List<string> Calls { get; } = [];

		public GitResult Run(string workingDirectory, IReadOnlyList<string> arguments, TimeSpan timeout)
		{
			Calls.Add(string.Join(' ', arguments));
			return results[Math.Min(Calls.Count - 1, results.Length - 1)];
		}
	}

	[Fact]
	public void TC_HS_21_ACommitMissingFromTheRepository_IsUnknown_WithTheRealGit()
	{
		var first = InitRepository();

		var result = Evaluate(first, MissingCommit);

		Assert.Equal(DaemonChangeDecision.Unknown, result.Decision);
		Assert.Contains("exit code", result.Reason);
	}

	[Fact]
	public void TC_HS_21_ASourceLocationThatIsNoRepository_IsUnknown_WithTheRealGit()
	{
		Write("code/TessynDesktop/TessynDesktop.csproj", Project());

		var result = Evaluate("1111111", "2222222");

		Assert.Equal(DaemonChangeDecision.Unknown, result.Decision);
	}

	[Fact]
	public void TC_HS_21_AnOverallTimeLimitThatIsUsedUp_GivesUnknown_WithoutRunningGit()
	{
		var git = new FakeGit(new GitResult(true, false, 0, ""));
		_commits.Commits[Path.GetFullPath(_running)] = "1111111";
		_commits.Commits[Path.GetFullPath(_buildFolder)] = "2222222";
		Write("code/TessynDesktop/TessynDesktop.csproj", Project());

		var result = Detector(git, TimeSpan.Zero).Evaluate(_running, _buildFolder, _source);

		Assert.Equal(DaemonChangeDecision.Unknown, result.Decision);
		Assert.Empty(git.Calls);
	}

	[Fact]
	public void TC_HS_21_TheRealGitRunner_KillsAGitThatRunsLongerThanTheLimit_AndReportsAMissingFolder()
	{
		var runner = new ProcessGitRunner();
		var clock = Stopwatch.StartNew();

		var slow = runner.Run(_root, ["-c", "alias.slow=!ping -n 20 127.0.0.1 >/dev/null", "slow"], TimeSpan.FromMilliseconds(600));

		Assert.True(slow.TimedOut);
		Assert.False(slow.Completed);
		Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10));
		Assert.False(runner.Run(Path.Combine(_root, "missing"), ["--version"], TimeSpan.FromSeconds(10)).Started);
		var version = runner.Run(_root, ["--version"], TimeSpan.FromSeconds(10));
		Assert.True(version.Completed);
		Assert.StartsWith("git version", version.Output);
	}

	// ---- the commit reader ----

	[Theory]
	[InlineData("1.0.0+13fd7d07a1b2c3d4e5f60718293a4b5c6d7e8f90", "13fd7d07a1b2c3d4e5f60718293a4b5c6d7e8f90")]
	[InlineData("1.0.0+13FD7D07", "13fd7d07")]
	[InlineData("1.0.0", null)]
	[InlineData("1.0.0+", null)]
	[InlineData("1.0.0+abc", null)]
	[InlineData("1.0.0+nothex12", null)]
	[InlineData("", null)]
	[InlineData(null, null)]
	public void CommitOf_TakesTheHexCommitAfterThePlus(string? version, string? expected)
	{
		Assert.Equal(expected, AssemblyDaemonCommitReader.CommitOf(version));
	}

	[Fact]
	public void TheReader_ReadsTheCommitFromAnAssemblyFileWithoutLoadingIt_AndNullFromABrokenOne()
	{
		var folder = Path.Combine(_root, "reader");
		Directory.CreateDirectory(folder);
		var wpf = typeof(DaemonRestarter).Assembly;
		File.Copy(wpf.Location, Path.Combine(folder, "TessynDesktop.dll"));
		var expected = AssemblyDaemonCommitReader.CommitOf(wpf.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);

		var commit = new AssemblyDaemonCommitReader().ReadCommit(folder);

		Assert.NotNull(expected);
		Assert.Equal(expected, commit);

		var broken = Path.Combine(_root, "broken");
		Directory.CreateDirectory(broken);
		File.WriteAllText(Path.Combine(broken, "TessynDesktop.dll"), "not an assembly");
		Assert.Null(new AssemblyDaemonCommitReader().ReadCommit(broken));
		Assert.Null(new AssemblyDaemonCommitReader().ReadCommit(Path.Combine(_root, "nothing")));
	}

	// ---- TC.HS.22 ----

	[Fact]
	public void TC_HS_22_TheDecisionIsLogged_WithBothCommitsAndTheReason()
	{
		var logger = new CapturingLogger<DaemonRestarter>();
		var detector = new FakeDetector(() => new DaemonChangeResult(DaemonChangeDecision.Unchanged, "aaaaaaa1", "bbbbbbb2", "no change in the daemon source"));

		Assert.Null(Restarter(detector, logger).Check(1));

		var message = Assert.Single(logger.Messages);
		Assert.Contains("aaaaaaa1", message);
		Assert.Contains("bbbbbbb2", message);
		Assert.Contains("Unchanged", message);
		Assert.Contains("no change in the daemon source", message);
	}

	[Fact]
	public void TC_HS_22_AFallbackIsLogged_AsTheTimeRuleDeciding()
	{
		var logger = new CapturingLogger<DaemonRestarter>();
		var detector = new FakeDetector(() => new DaemonChangeResult(DaemonChangeDecision.Unknown, null, "bbbbbbb2", "a commit is unknown"));

		Assert.NotNull(Restarter(detector, logger).Check(1));

		var message = Assert.Single(logger.Messages);
		Assert.Contains("unknown", message);
		Assert.Contains("Unknown", message);
		Assert.Contains("the build time decides", message);
	}

	[Fact]
	public async Task TC_HS_22_TheCheckRunsOffTheUiThread_TheConnectionDoesNotWaitForIt()
	{
		using var release = new ManualResetEventSlim(false);
		var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		var detector = new FakeDetector(() =>
		{
			started.TrySetResult(Environment.CurrentManagedThreadId);
			Assert.True(release.Wait(TimeSpan.FromSeconds(20)));
			return new DaemonChangeResult(DaemonChangeDecision.Changed, "aaaaaaa1", "bbbbbbb2", "the daemon source differs");
		});
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.Found, new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "token", "run", 4321)));
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(new FakeTessynClient()), new ImmediateUiDispatcher(), NullLogger<ConnectionViewModel>.Instance,
			TimeSpan.FromMilliseconds(5), null, Restarter(detector));

		await connection.RunAsync(CancellationToken.None); // returns while the check still blocks
		var checkThread = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.NotEqual(Environment.CurrentManagedThreadId, checkThread);
		Assert.False(connection.IsDaemonOlder);
		release.Set();
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!connection.IsDaemonOlder && DateTime.UtcNow < deadline)
			await Task.Delay(20);
		Assert.True(connection.IsDaemonOlder);
	}

	[Fact]
	public async Task TC_HS_22_ACheckForANewerBuild_EvaluatesTheBannerAgain_AndTheBannerClearsWhenNothingChanged()
	{
		var decision = DaemonChangeDecision.Changed;
		var detector = new FakeDetector(() => new DaemonChangeResult(decision, "aaaaaaa1", "bbbbbbb2", "test"));
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.Found, new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "token", "run", 4321)));
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(new FakeTessynClient()), new ImmediateUiDispatcher(), NullLogger<ConnectionViewModel>.Instance,
			TimeSpan.FromMilliseconds(5), null, Restarter(detector));
		connection.RecheckDaemonBuild(); // not connected yet: nothing happens
		Assert.Equal(0, detector.Evaluations);

		await connection.RunAsync(CancellationToken.None);
		await WaitAsync(() => connection.IsDaemonOlder);

		decision = DaemonChangeDecision.Unchanged;
		await WaitAsync(() =>
		{
			connection.RecheckDaemonBuild();
			return !connection.IsDaemonOlder;
		});
		Assert.False(connection.IsDaemonOlder);
		Assert.True(detector.Evaluations >= 2);
	}

	private static async Task WaitAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!condition() && DateTime.UtcNow < deadline)
			await Task.Delay(20);
		Assert.True(condition());
	}
}
