using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>Pure matching, selection and ordering rules of the Tags tab (FR.24.11, FR.24.12).</summary>
/// <remarks>Created by Claude</remarks>
public static class SessionTagSearch
{
	/// <summary>FR.24.11 — every whitespace-separated part of the query occurs, case-insensitively, in tag.Label or tag.Key; null/blank matches all.</summary>
	public static bool Matches(SessionTagModel tag, string? query)
	{
		if (string.IsNullOrWhiteSpace(query))
			return true;

		var parts = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
		foreach (var part in parts)
		{
			var inLabel = tag.Label?.Contains(part, StringComparison.OrdinalIgnoreCase) == true;
			var inKey   = tag.Key?.Contains(part, StringComparison.OrdinalIgnoreCase) == true;
			if (!inLabel && !inKey)
				return false;
		}
		return true;
	}

	/// <summary>FR.24.12 — the NodeIds carrying at least one of the selected keys (keys compare case-insensitively; NodeIds ordinal). Empty selection = empty set.</summary>
	public static IReadOnlySet<string> SessionsWithAnyTag(IEnumerable<SessionTagUsageModel> index, IEnumerable<string> selectedKeys)
	{
		var result = new HashSet<string>(StringComparer.Ordinal);
		var keys   = new HashSet<string>(selectedKeys, StringComparer.OrdinalIgnoreCase);
		if (keys.Count == 0)
			return result;

		foreach (var usage in index)
			if (keys.Contains(usage.Tag.Key))
				result.UnionWith(usage.NodeIds);
		return result;
	}

	/// <summary>FR.24.11 — kind (enum order), then newest Last Activity among usage.NodeIds (null = no activity, sorts last), then label (OrdinalIgnoreCase).</summary>
	public static IReadOnlyList<SessionTagUsageModel> Order(IEnumerable<SessionTagUsageModel> usages, Func<string, DateTimeOffset?> lastActivityOf)
	{
		return usages
			.Select(u => (Usage: u, Latest: LatestActivity(u, lastActivityOf)))
			.OrderBy(x => x.Usage.Tag.Kind)
			.ThenByDescending(x => x.Latest.HasValue)
			.ThenByDescending(x => x.Latest ?? DateTimeOffset.MinValue)
			.ThenBy(x => x.Usage.Tag.Label, StringComparer.OrdinalIgnoreCase)
			.Select(x => x.Usage)
			.ToList();
	}

	private static DateTimeOffset? LatestActivity(SessionTagUsageModel usage, Func<string, DateTimeOffset?> lastActivityOf)
	{
		DateTimeOffset? latest = null;
		foreach (var id in usage.NodeIds)
		{
			var t = lastActivityOf(id);
			if (t.HasValue && (!latest.HasValue || t.Value > latest.Value))
				latest = t;
		}
		return latest;
	}
}
