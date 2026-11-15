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

	public AttentionRpcModule(
		ISessionRegistry registry,
		IAttentionService attention,
		IAttentionReplyService reply,
		ITurnOptionsResolver options,
		IAttentionOverviewService? overview = null)
	{
		_registry  = registry;
		_attention = attention;
		_reply     = reply;
		_options   = options;
		_overview  = overview;
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
			_attention.GetItems(node.NodeId).Select(item => item.ToDto()).ToList());
	}

	private async Task<object?> ReplyAsync(JsonNode? @params)
	{
		var node    = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));
		var answers = ParseAnswers(TdcpParams.Find(@params, "answers"));
		var options = await _options.ResolveAsync(@params);

		return _reply.Reply(node, answers, options);
	}

	private object? Dismiss(JsonNode? @params)
	{
		var node   = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));
		var itemId = TdcpParams.RequireString(@params, "itemId");

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
				TdcpParams.RequireString(element, "text")));
		}

		return answers;
	}
}
