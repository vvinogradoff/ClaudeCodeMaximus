using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Decides which entries of a Claude JSONL still have to be written back to the session .txt file
/// (ISSUE.009, ISSUE.014). The write-back exists for turns run outside Tessyn; every Tessyn-run turn
/// (interactive, headless, or redirected into a live streaming process) is already persisted by the Core.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class JsonlWriteBackPlanner
{
	/// <summary>
	/// Outside headless turns, entries past the baseline are filtered by identity (role plus trimmed content) against the tail
	/// of the .txt file; each tail entry absorbs at most one candidate, so a genuinely repeated message
	/// from an external turn is still written once. The baseline always advances to the JSONL count.
	/// While a headless Tessyn turn runs, every SYSTEM entry (tool line) past the baseline is written with no
	/// tail dedupe (the advancing baseline already offers each entry once, and repeated identical lines such as
	/// a polling loop must stay); USER and ASSISTANT are skipped because the Core writes them itself.
	/// </summary>
	/// <param name="jsonlEntries">Displayable JSONL entries, in file order.</param>
	/// <param name="lastKnownCount">Baseline count from the previous pass.</param>
	/// <param name="txtTail">Trailing entries currently stored in the .txt file.</param>
	/// <param name="isHeadlessTurnActive">True while a Core-run headless turn is in progress and the session is not sending interactively.</param>
	public JsonlWriteBackPlanModel Plan(
		IReadOnlyList<SessionEntryModel> jsonlEntries,
		int lastKnownCount,
		IReadOnlyList<SessionEntryModel> txtTail,
		bool isHeadlessTurnActive)
	{
		if (jsonlEntries.Count <= lastKnownCount)
			return new JsonlWriteBackPlanModel { EntriesToWrite = [], NewBaselineCount = lastKnownCount };

		var candidates = jsonlEntries.Skip(lastKnownCount);
		if (isHeadlessTurnActive)
			return new JsonlWriteBackPlanModel
			{
				EntriesToWrite   = candidates.Where(e => e.Role == Constants.SessionFile.RoleSystem).ToList(),
				NewBaselineCount = jsonlEntries.Count,
			};

		var remaining = new List<SessionEntryModel>(txtTail);
		var toWrite   = new List<SessionEntryModel>();
		foreach (var candidate in candidates)
		{
			var candidateKey = IdentityOf(candidate.Content);
			var match = remaining.FindIndex(e => e.Role == candidate.Role && IdentityOf(e.Content) == candidateKey);
			if (match >= 0)
				remaining.RemoveAt(match);
			else
				toWrite.Add(candidate);
		}

		return new JsonlWriteBackPlanModel { EntriesToWrite = toWrite, NewBaselineCount = jsonlEntries.Count };
	}

	/// <summary>
	/// The comparison form of an entry's content: trimmed, with line endings normalised to LF. The .txt reader rebuilds a multi-line
	/// entry with <see cref="Environment.NewLine"/> (CRLF on Windows) while the JSONL text carries LF, so without this a multi-line entry
	/// the Core already wrote never matched its JSONL twin and was appended a second time (the duplicate final answer).
	/// </summary>
	internal static string IdentityOf(string content) => content.ReplaceLineEndings("\n").Trim();
}
