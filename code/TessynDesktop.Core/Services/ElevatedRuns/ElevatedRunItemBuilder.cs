using System;
using System.Linq;
using System.Text;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Builds the Focus item of an elevated run (FR.23.31): an <see cref="AttentionKind.Ask"/> with <see cref="AttentionSource.ElevatedRun"/>, the key <c>elevated:&lt;id&gt;</c>,
/// and a body that also holds the facts, so a client that does not know the action is never blind. The command is in a code fence one backtick longer than the
/// longest run of backticks in it (at least three), so it is intact even where the body is rendered as markdown.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunItemBuilder
{
	/// <param name="requesterShortId">The requester's Short Node Id as <c>list_sessions</c> shows it.</param>
	/// <param name="mirrored">True for the copy in the top-level supervisor's ledger: it names the worker and makes no sound.</param>
	public AttentionPostRequestModel Build(ElevatedRunModel run, string requesterShortId, bool mirrored)
	{
		var title = string.Format(Constants.ElevatedRuns.ItemTitleFormat, Cut(run.Command, Constants.ElevatedRuns.ItemTitleCommandLength));

		var body = new StringBuilder()
			.Append(string.Format(Constants.ElevatedRuns.ItemReasonFormat, run.Reason)).Append('\n')
			.Append(string.Format(Constants.ElevatedRuns.ItemWorkingDirFormat, run.WorkingDir)).Append('\n')
			.Append(string.Format(mirrored ? Constants.ElevatedRuns.ItemRequestedByWorkerFormat : Constants.ElevatedRuns.ItemRequestedByFormat,
				run.RequesterName, requesterShortId)).Append('\n')
			.Append(Constants.ElevatedRuns.ItemApproveNote).Append("\n\n");

		var fence = new string('`', Math.Max(Constants.ElevatedRuns.ItemMinFenceLength, LongestBacktickRun(run.Command) + 1));
		body.Append(fence).Append('\n').Append(run.Command).Append('\n').Append(fence);

		return new AttentionPostRequestModel
		{
			Kind          = AttentionKind.Ask,
			Title         = title,
			Body          = body.ToString(),
			Key           = Constants.ElevatedRuns.ItemKeyPrefix + run.Id,
			Source        = AttentionSource.ElevatedRun,
			ElevatedRunId = run.Id,
			IsSilent      = mirrored,
			SystemText    = string.Format(Constants.ElevatedRuns.SystemEntryFormat, run.Id, run.Reason),
		};
	}

	private static int LongestBacktickRun(string text)
	{
		var longest = 0;
		var current = 0;
		foreach (var c in text)
		{
			current = c == '`' ? current + 1 : 0;
			longest = Math.Max(longest, current);
		}

		return longest;
	}

	private static string Cut(string text, int maxLength) => text.Length > maxLength ? text[..maxLength] : text;
}
