using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>Remote methods of a session's Attention Ledger: list, reply (Send answers) and dismiss (contract sections 3.17 to 3.19).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionRpcModule : IRemoteRpcModule
{
	private readonly ISessionRegistry _registry;
	private readonly IAttentionService _attention;
	private readonly IAttentionReplyService _reply;
	private readonly ITurnOptionsResolver _options;
	private readonly IAttentionOverviewService? _overview;
	private readonly ISessionModelSettings _modelSettings;
	private readonly IElevatedRunService? _elevatedRuns;

	public AttentionRpcModule(
		ISessionRegistry registry,
		IAttentionService attention,
		IAttentionReplyService reply,
		ITurnOptionsResolver options,
		ISessionModelSettings modelSettings,
		IAttentionOverviewService? overview = null,
		IElevatedRunService? elevatedRuns = null)
	{
		_registry  = registry;
		_attention = attention;
		_reply     = reply;
		_options   = options;
		_overview  = overview;
		_modelSettings = modelSettings;
		_elevatedRuns  = elevatedRuns;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync(Constants.Remote.Methods.AttentionList, List, remoteCallable: true);
		registry.Add(Constants.Remote.Methods.AttentionReply, async (p, _) => await ReplyAsync(p), remoteCallable: true);
		registry.AddSync(Constants.Remote.Methods.AttentionDismiss, Dismiss, remoteCallable: true);

		// Package J4: the items of every session in one list. Loopback only in this wave (a candidate for the relay with an FR.25 amendment).
		if (_overview != null)
			registry.AddSync(RemoteProtocol.AttentionAll.ListAllMethod, ListAll, remoteCallable: false);
	}

	private object? ListAll(JsonNode? @params) =>
		_overview!.ListAll(
			TdcpParams.OptionalString(@params, "status"),
			TdcpListParams.OptionalStrings(@params, "kinds"),
			TdcpParams.OptionalInt(@params, "limit"),
			TdcpParams.OptionalString(@params, "afterId"));

	private object? List(JsonNode? @params)
	{
		var node = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));

		return new AttentionListResult(
			node.NodeId,
			_attention.GetSummary(node.NodeId).ToDto(),
			_attention.GetItems(node.NodeId).Select(ToItemDto).ToList());
	}

	/// <summary>The item with its action (tdcp-spec 10.A13): the elevated run behind it, without paths over the relay.</summary>
	private AttentionItemDto ToItemDto(AttentionItemModel item)
	{
		var dto = item.ToDto();
		if (item.ElevatedRunId == null || _elevatedRuns?.Find(item.ElevatedRunId) is not { } run)
			return dto;

		var relay = TdcpCallContext.Transport != TdcpTransport.Loopback;
		var action = new AttentionActionDto(RemoteProtocol.ElevatedRuns.ActionType, run.ToDto(_elevatedRuns.GetLogPath(run.Id), relay, _elevatedRuns.ShortIdOf(run.RequesterNodeId)));

		// The body also holds the working directory (FR.23.31); over the relay it shows the folder name only: no path leaves the machine (tdcp-spec 8.2).
		return dto with { Action = action, Body = relay && dto.Body != null ? dto.Body.Replace(run.WorkingDir, ElevatedRunDtoMapping.FolderName(run.WorkingDir), StringComparison.Ordinal) : dto.Body };
	}

	/// <summary>FR.23.31: the card of an elevated run is answered with Run as administrator or Decline, never with a reply or a dismiss.</summary>
	private void RefuseElevatedItem(string nodeId, string itemId)
	{
		var id = itemId.Trim();
		if (_attention.GetItems(nodeId).Any(i => i.ElevatedRunId != null && string.Equals(i.Id, id, System.StringComparison.OrdinalIgnoreCase)))
			throw new TdcpInvalidParamsException(RemoteProtocol.ElevatedRuns.ItemRefusalText);
	}

	private async Task<object?> ReplyAsync(JsonNode? @params)
	{
		var node    = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));
		var answers = ParseAnswers(TdcpParams.Find(@params, "answers"));
		foreach (var answer in answers)
			RefuseElevatedItem(node.NodeId, answer.ItemId);
		var options = await _options.ResolveAsync(@params);
		var result  = _reply.Reply(node, answers, options);

		// FR.12.35: the picks of the turn the answers started are kept on the session, as for a send.
		if (result.TurnStarted)
			_modelSettings.RecordPicks(node, options);

		return result;
	}

	private object? Dismiss(JsonNode? @params)
	{
		var node   = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));
		var itemId = TdcpParams.RequireString(@params, "itemId");
		RefuseElevatedItem(node.NodeId, itemId);

		return new AttentionDismissResult(_attention.CloseByUser(node, itemId.Trim()));
	}

	private static List<AttentionAnswerParams> ParseAnswers(JsonNode? node)
	{
		if (node is not JsonArray array)
			throw new TdcpInvalidParamsException("'answers' must be an array");

		var answers = new List<AttentionAnswerParams>(array.Count);
		foreach (var element in array)
		{
			answers.Add(new AttentionAnswerParams(
				TdcpParams.RequireString(element, "itemId"),
				TdcpParams.RequireString(element, "text"),
				TdcpQuestionAnswerParams.Optional(element)));
		}

		return answers;
	}
}
