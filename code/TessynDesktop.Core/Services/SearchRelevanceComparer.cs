using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Orders search relevance keys most relevant first (FR.1.12): matches before non-matches, any strict
/// match before none, then strict count, loose count and last prompt time, all descending (null last).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SearchRelevanceComparer : IComparer<SearchRelevanceKeyModel?>
{
	public int Compare(SearchRelevanceKeyModel? x, SearchRelevanceKeyModel? y)
	{
		if (x is null || y is null)
			return x is null ? (y is null ? 0 : 1) : -1;

		var strictTier = (y.StrictMatchCount > 0).CompareTo(x.StrictMatchCount > 0);
		if (strictTier != 0)
			return strictTier;

		var strict = y.StrictMatchCount.CompareTo(x.StrictMatchCount);
		if (strict != 0)
			return strict;

		var loose = y.LooseMatchCount.CompareTo(x.LooseMatchCount);
		if (loose != 0)
			return loose;

		if (x.LastPromptTimestamp is null || y.LastPromptTimestamp is null)
			return x.LastPromptTimestamp is null ? (y.LastPromptTimestamp is null ? 0 : 1) : -1;

		return y.LastPromptTimestamp.Value.CompareTo(x.LastPromptTimestamp.Value);
	}
}
