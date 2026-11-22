using System.Globalization;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>The words of the elevated-run card (FR.23.32): the closed card's outcome line and the durations. Pure functions of the run.</summary>
/// <remarks>Created by Claude</remarks>
public static class ElevatedRunText
{
	/// <summary><c>1h 02m 05s</c>, <c>3m 12s</c>, <c>7s</c> (the same reading as the daemon's outcome turn).</summary>
	public static string FormatDuration(TimeSpan duration)
	{
		var seconds = (long)Math.Floor(Math.Max(0, duration.TotalSeconds));
		var hours = seconds / 3600;
		var minutes = seconds % 3600 / 60;
		var rest = seconds % 60;
		if (hours > 0)
			return string.Create(CultureInfo.InvariantCulture, $"{hours}h {minutes:00}m {rest:00}s");

		return minutes > 0
			? string.Create(CultureInfo.InvariantCulture, $"{minutes}m {rest:00}s")
			: string.Create(CultureInfo.InvariantCulture, $"{rest}s");
	}

	/// <summary>True for a status that never changes again (everything after <see cref="ElevatedRunStatus.Running"/>).</summary>
	public static bool IsFinal(ElevatedRunStatus status) => status > ElevatedRunStatus.Running;

	/// <summary>The outcome line of a finished run (<c>Completed: exit code 0, 3m 12s</c>, <c>Declined</c>, ...); empty while the run is open.</summary>
	public static string OutcomeLine(ElevatedRunDto run) => run.Status switch
	{
		ElevatedRunStatus.Completed => CompletedLine(run),
		ElevatedRunStatus.Declined => Constants.ElevatedRuns.OutcomeDeclined,
		ElevatedRunStatus.DeclinedAtPrompt => Constants.ElevatedRuns.OutcomeDeclinedAtPrompt,
		ElevatedRunStatus.FailedToStart => string.IsNullOrWhiteSpace(run.Failure)
			? Constants.ElevatedRuns.OutcomeFailed
			: string.Format(Constants.ElevatedRuns.OutcomeFailedFormat, run.Failure),
		ElevatedRunStatus.TimedOut => string.Format(Constants.ElevatedRuns.OutcomeTimedOutFormat, run.TimeoutMinutes),
		ElevatedRunStatus.Cancelled => run.CancelledBy == ElevatedRunCancelledBy.Agent
			? Constants.ElevatedRuns.OutcomeWithdrawnByAgent
			: Constants.ElevatedRuns.OutcomeStoppedByUser,
		ElevatedRunStatus.Expired => Constants.ElevatedRuns.OutcomeExpired,
		_ => string.Empty,
	};

	private static string CompletedLine(ElevatedRunDto run)
	{
		if (run.ExitCode is not { } code)
			return Constants.ElevatedRuns.OutcomeCompleted;

		return run is { StartedUtc: { } started, EndedUtc: { } ended } && ended >= started
			? string.Format(Constants.ElevatedRuns.OutcomeCompletedExitDurationFormat, code, FormatDuration(ended - started))
			: string.Format(Constants.ElevatedRuns.OutcomeCompletedExitFormat, code);
	}
}
