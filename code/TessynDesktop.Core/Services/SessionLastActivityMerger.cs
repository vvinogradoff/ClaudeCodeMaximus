using System;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Combines a slow background scan with the live values a view model gained while the scan ran (ISSUE.016).
/// A timestamp is replaced only by a strictly newer scanned one (or filled when missing); a null scan result never
/// overwrites a value. The profile name travels with the last-prompt timestamp.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionLastActivityMerger
{
	public SessionLastActivityModel Merge(SessionLastActivityModel current, SessionLastActivityModel scanned)
	{
		var promptWins = Wins(current.LastPromptTimestamp, scanned.LastPromptTimestamp);
		return new SessionLastActivityModel(
			current.FileName,
			promptWins ? scanned.LastPromptTimestamp : current.LastPromptTimestamp,
			Wins(current.LastResponseTimestamp, scanned.LastResponseTimestamp) ? scanned.LastResponseTimestamp : current.LastResponseTimestamp,
			promptWins ? scanned.ProfileName : current.ProfileName,
			Wins(current.LastDraftEditTimestamp, scanned.LastDraftEditTimestamp) ? scanned.LastDraftEditTimestamp : current.LastDraftEditTimestamp);
	}

	private static bool Wins(DateTimeOffset? current, DateTimeOffset? scanned)
		=> scanned.HasValue && (!current.HasValue || scanned.Value > current.Value);
}
