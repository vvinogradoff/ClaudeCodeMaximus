using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>
/// Outcome of <see cref="TessynDesktop.Services.JsonlWriteBackPlanner.Plan"/>: the JSONL entries that
/// still need to reach the session .txt file and the baseline count to remember for the next pass (ISSUE.014).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class JsonlWriteBackPlanModel
{
	public required IReadOnlyList<SessionEntryModel> EntriesToWrite { get; init; }

	/// <summary>Value for the caller's "last known JSONL entry count" after this pass.</summary>
	public required int NewBaselineCount { get; init; }
}
