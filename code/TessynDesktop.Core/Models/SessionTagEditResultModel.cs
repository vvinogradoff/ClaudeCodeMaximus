using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>Result of <c>ISessionTagService.Edit</c> (FR.24.4): per-entry outcomes (adds first, then removes), the labels actually added and removed, and the tags afterwards.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagEditResultModel
{
	public IReadOnlyList<SessionTagEntryOutcomeModel> Outcomes { get; init; } = [];
	public IReadOnlyList<string> AddedLabels { get; init; } = [];
	public IReadOnlyList<string> RemovedLabels { get; init; } = [];

	/// <summary>Name-derived plus declared tags after the edit, in FR.24.7 order.</summary>
	public IReadOnlyList<SessionTagModel> Tags { get; init; } = [];

	public bool HasChanges => AddedLabels.Count > 0 || RemovedLabels.Count > 0;
}
