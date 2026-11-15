using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IAttentionOverviewService" />
/// <remarks>Created by Claude</remarks>
public sealed class AttentionOverviewService : IAttentionOverviewService
{
	private readonly IRemoteTreeReader _tree;
	private readonly IAttentionService _attention;

	public AttentionOverviewService(IRemoteTreeReader tree, IAttentionService attention)
	{
		_tree      = tree;
		_attention = attention;
	}

	/// <inheritdoc />
	public AttentionListAllResult ListAll(string? status, IReadOnlyList<string>? kinds, int? limit, string? afterId)
	{
		var wanted = ParseStatus(status);
		var wantedKinds = ParseKinds(kinds);
		var take = Math.Clamp(limit ?? Constants.AttentionAll.DefaultLimit, 1, Constants.AttentionAll.MaxLimit);

		var rows = new List<(string NodeId, string SessionName, AttentionItemModel Item)>();
		foreach (var session in _tree.Snapshot().AllSessions)
		{
			// An open-only listing skips the many sessions without an open item before copying their ledgers.
			if (wanted == AttentionStatus.Open && _attention.GetSummary(session.NodeId).AllOpenCount == 0)
				continue;

			foreach (var item in _attention.GetItems(session.NodeId))
			{
				if (item.Status == wanted && (wantedKinds == null || wantedKinds.Contains(item.Kind)))
					rows.Add((session.NodeId, session.Name, item));
			}
		}

		var ordered = rows
			.OrderBy(r => r.Item.Kind)
			.ThenByDescending(r => r.Item.UpdatedUtc)
			.ThenBy(r => r.NodeId, StringComparer.Ordinal)
			.ThenBy(r => r.Item.Id, StringComparer.Ordinal)
			.ToList();

		var start = 0;
		if (!string.IsNullOrEmpty(afterId))
		{
			var at = ordered.FindIndex(r => CursorOf(r.NodeId, r.Item) == afterId);
			start = at >= 0 ? at + 1 : throw new TdcpInvalidParamsException(Constants.AttentionAll.BadCursor);
		}

		var page = ordered.Skip(start).Take(take).ToList();
		var next = start + page.Count < ordered.Count && page.Count > 0 ? CursorOf(page[^1].NodeId, page[^1].Item) : null;
		return new AttentionListAllResult([.. page.Select(r => new AttentionAllItemDto(r.NodeId, r.SessionName, r.Item.ToDto()))], next);
	}

	private static string CursorOf(string nodeId, AttentionItemModel item) => nodeId + Constants.AttentionAll.CursorSeparator + item.Id;

	private static AttentionStatus ParseStatus(string? status) =>
		string.IsNullOrEmpty(status) ? AttentionStatus.Open
			: Enum.TryParse<AttentionStatus>(status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed
			: throw new TdcpInvalidParamsException(Constants.AttentionAll.BadStatus);

	private static HashSet<AttentionKind>? ParseKinds(IReadOnlyList<string>? kinds)
	{
		if (kinds == null || kinds.Count == 0)
			return null;

		var result = new HashSet<AttentionKind>();
		foreach (var kind in kinds)
		{
			if (!Enum.TryParse<AttentionKind>(kind, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
				throw new TdcpInvalidParamsException(Constants.AttentionAll.BadKind);
			result.Add(parsed);
		}

		return result;
	}
}
