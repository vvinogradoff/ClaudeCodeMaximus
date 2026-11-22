using System;
using System.Globalization;
using System.Text;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// The words and the text of the outcome of an elevated run (FR.15.19, FR.23.31): the status words of the header and of the closing note of the Focus items, the
/// duration, and the prompt of the one turn the requesting session receives.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunOutcomeBuilder(ElevatedRunOutputReader output)
{
	/// <summary>The status words of a final run: <c>completed</c>, <c>declined by the user</c>, <c>failed to start: ...</c>, and so on.</summary>
	public string StatusWords(ElevatedRunModel run) => run.Status switch
	{
		ElevatedRunStatus.Completed        => Constants.ElevatedRuns.WordsCompleted,
		ElevatedRunStatus.Declined         => Constants.ElevatedRuns.WordsDeclined,
		ElevatedRunStatus.DeclinedAtPrompt => Constants.ElevatedRuns.WordsDeclinedAtPrompt,
		ElevatedRunStatus.FailedToStart    => string.Format(Constants.ElevatedRuns.WordsFailedFormat, run.Failure),
		ElevatedRunStatus.TimedOut         => string.Format(Constants.ElevatedRuns.WordsTimedOutFormat, run.TimeoutMinutes),
		ElevatedRunStatus.Cancelled        => run.CancelledBy == ElevatedRunCancelledBy.Agent ? Constants.ElevatedRuns.WordsCancelledByAgent : Constants.ElevatedRuns.WordsCancelledByUser,
		ElevatedRunStatus.Expired          => Constants.ElevatedRuns.WordsExpired,
		_                                  => Constants.ElevatedRuns.StatusNames[(int)run.Status],
	};

	/// <summary>The closing note of the Focus items: <c>Elevated run &lt;id&gt;: &lt;words&gt;[, exit code &lt;n&gt;, &lt;duration&gt;]</c> (FR.23.31).</summary>
	public string ResolutionNote(ElevatedRunModel run)
	{
		var note = string.Format(Constants.ElevatedRuns.ResolutionNoteFormat, run.Id, StatusWords(run));
		return run.ExitCode is { } code && Duration(run) is { } duration
			? note + string.Format(Constants.ElevatedRuns.ResolutionNoteExitFormat, code, FormatDuration(duration))
			: note;
	}

	/// <summary>The time the process ran, null when it did not start or has not ended.</summary>
	public TimeSpan? Duration(ElevatedRunModel run) =>
		run is { StartedUtc: { } started, EndedUtc: { } ended } && ended >= started ? ended - started : null;

	/// <summary><c>1h 02m 05s</c>, <c>41m 05s</c>, <c>7s</c>.</summary>
	public string FormatDuration(TimeSpan duration)
	{
		var seconds = (long)Math.Floor(duration.TotalSeconds);
		var hours   = seconds / 3600;
		var minutes = seconds % 3600 / 60;
		var rest    = seconds % 60;
		if (hours > 0)
			return string.Create(CultureInfo.InvariantCulture, $"{hours}h {minutes:00}m {rest:00}s");

		return minutes > 0
			? string.Create(CultureInfo.InvariantCulture, $"{minutes}m {rest:00}s")
			: string.Create(CultureInfo.InvariantCulture, $"{rest}s");
	}

	/// <summary>The prompt of the outcome turn (FR.15.19).</summary>
	public string BuildPrompt(ElevatedRunModel run, string logPath)
	{
		var sb = new StringBuilder();
		sb.Append(string.Format(Constants.ElevatedRuns.OutcomeHeaderFormat, run.Id, StatusWords(run), run.Reason));
		sb.Append('\n').Append(string.Format(Constants.ElevatedRuns.OutcomeCommandFormat, Cut(run.Command, Constants.ElevatedRuns.OutcomeCommandLength)));
		sb.Append('\n').Append(string.Format(Constants.ElevatedRuns.OutcomeWorkingDirFormat, run.WorkingDir));
		if (run is { Status: ElevatedRunStatus.Declined, Note: { Length: > 0 } note })
			sb.Append('\n').Append(string.Format(Constants.ElevatedRuns.OutcomeNoteFormat, note));

		if (run.StartedUtc == null)
			return sb.ToString();

		if (run.ExitCode is { } code)
			sb.Append('\n').Append(string.Format(Constants.ElevatedRuns.OutcomeExitCodeFormat, code));
		if (Duration(run) is { } duration)
			sb.Append('\n').Append(string.Format(Constants.ElevatedRuns.OutcomeDurationFormat, FormatDuration(duration)));
		sb.Append('\n').Append(string.Format(Constants.ElevatedRuns.OutcomeLogFormat, logPath));

		var tail = output.ReadTail(logPath, Constants.ElevatedRuns.OutcomeTailLines, Constants.ElevatedRuns.OutcomeTailLineLength, Constants.ElevatedRuns.OutcomeTailMaxChars);
		if (tail.Count > 0)
			sb.Append('\n').Append(string.Format(Constants.ElevatedRuns.OutcomeOutputHeaderFormat, tail.Count)).Append('\n').Append(string.Join('\n', tail));

		return sb.ToString();
	}

	private static string Cut(string text, int maxLength) => text.Length > maxLength ? text[..maxLength] : text;
}
