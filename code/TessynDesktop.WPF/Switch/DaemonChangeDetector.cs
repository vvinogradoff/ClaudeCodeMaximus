using System.Diagnostics;

namespace TessynDesktop.WPF.Switch;

/// <summary>
/// FR.32.9: the commits of the running daemon and of the newest build; when they differ, git compares them over the daemon's source paths, and uncommitted changes in those
/// paths count as changed (with equal commits they leave the decision to the time rule). Anything unknown, any git failure and the time limit give <see cref="DaemonChangeDecision.Unknown"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonChangeDetector : IDaemonChangeDetector
{
	private const int DifferenceExitCode = 1;

	private readonly IDaemonCommitReader _commits;
	private readonly IGitRunner _git;
	private readonly DaemonSourcePathResolver _paths;
	private readonly TimeSpan _timeout;

	public DaemonChangeDetector(IDaemonCommitReader commits, IGitRunner git, DaemonSourcePathResolver paths, TimeSpan timeout)
	{
		_commits = commits;
		_git = git;
		_paths = paths;
		_timeout = timeout;
	}

	public DaemonChangeResult Evaluate(string runningFolder, string buildFolder, string sourceLocation)
	{
		var running = _commits.ReadCommit(runningFolder);
		var newest = _commits.ReadCommit(buildFolder);
		DaemonChangeResult Result(DaemonChangeDecision decision, string reason) => new(decision, running, newest, reason);

		if (running == null || newest == null)
			return Result(DaemonChangeDecision.Unknown, "a commit is unknown");

		var paths = _paths.Resolve(sourceLocation);
		if (paths == null)
			return Result(DaemonChangeDecision.Unknown, "the daemon's source paths cannot be derived");

		var clock = Stopwatch.StartNew();
		var sameCommit = string.Equals(running, newest, StringComparison.OrdinalIgnoreCase);
		if (!sameCommit)
		{
			var diff = Run(sourceLocation, clock, ["diff", "--quiet", running, newest, "--", .. paths]);
			if (!diff.Completed)
				return Result(DaemonChangeDecision.Unknown, diff.Started ? "git timed out" : "git is not available");

			if (diff.ExitCode == DifferenceExitCode)
				return Result(DaemonChangeDecision.Changed, "the daemon's source differs between the commits");

			if (diff.ExitCode != 0)
				return Result(DaemonChangeDecision.Unknown, $"git diff failed with exit code {diff.ExitCode} (a commit missing from the repository?)");
		}

		var status = Run(sourceLocation, clock, ["--no-optional-locks", "status", "--porcelain", "--", .. paths]);
		if (!status.Completed)
			return Result(DaemonChangeDecision.Unknown, status.Started ? "git timed out" : "git is not available");

		if (status.ExitCode != 0)
			return Result(DaemonChangeDecision.Unknown, $"git status failed with exit code {status.ExitCode}");

		if (sameCommit)
		{
			// The bytes may differ although the commit is the same: the time rule decides.
			return string.IsNullOrWhiteSpace(status.Output)
				? Result(DaemonChangeDecision.Unchanged, "the same commit and no uncommitted changes in the daemon's source")
				: Result(DaemonChangeDecision.Unknown, "the same commit, but uncommitted changes in the daemon's source");
		}

		return string.IsNullOrWhiteSpace(status.Output)
			? Result(DaemonChangeDecision.Unchanged, "the daemon's source is the same in both commits and has no uncommitted changes")
			: Result(DaemonChangeDecision.Changed, "uncommitted changes in the daemon's source");
	}

	/// <summary>One git run within what is left of the overall time limit.</summary>
	private GitResult Run(string sourceLocation, Stopwatch clock, IReadOnlyList<string> arguments)
	{
		var left = _timeout - clock.Elapsed;
		return left <= TimeSpan.Zero ? GitResult.OutOfTime : _git.Run(sourceLocation, arguments, left);
	}
}
