using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The unsent draft of a session for a local client (package J2): <c>draft.get</c>, <c>draft.set</c> (optimistic, with <c>baseRevision</c>) and
/// <c>draft.delete</c>. Loopback only. With the optional <c>itemId</c> the same three methods address the reply draft of one Focus item (FR.23.19). The change notification (<c>draft.changed</c>) is published by <see cref="RemoteNotificationPublisher"/>
/// from the <see cref="IDraftService"/> events, so a write of the Avalonia composer reaches the clients like a write of a client does.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DraftRpcModule : IRemoteRpcModule
{
	private readonly ISessionRegistry _registry;
	private readonly IDraftService _drafts;
	private readonly IAttentionService? _attention;
	private readonly DraftRevisions _revisions = new();

	/// <param name="attention">The Attention Ledger, to refuse the reply draft of an unknown or closed item (FR.23.19); null accepts any well-formed item id.</param>
	public DraftRpcModule(ISessionRegistry registry, IDraftService drafts, IAttentionService? attention = null)
	{
		_registry  = registry;
		_drafts    = drafts;
		_attention = attention;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync(RemoteProtocol.Composer.DraftGetMethod, Get, remoteCallable: false);
		registry.AddSync(RemoteProtocol.Composer.DraftSetMethod, Set, remoteCallable: false);
		registry.AddSync(RemoteProtocol.Composer.DraftDeleteMethod, Delete, remoteCallable: false);
	}

	private object? Get(JsonNode? @params)
	{
		var node   = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));
		var itemId = OptionalItemId(@params, node);
		if (itemId != null)
		{
			var item = _drafts.LoadItemDraftFull(node.FileName, itemId);
			return new DraftGetResult(item?.Text ?? string.Empty, _revisions.Of(item), item?.Questions);
		}

		var text = _drafts.LoadDraft(node.FileName) ?? string.Empty;
		return new DraftGetResult(text, _revisions.Of(text));
	}

	private object? Set(JsonNode? @params)
	{
		var node         = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));
		var text         = TdcpParams.RequireString(@params, "text");
		var baseRevision = TdcpParams.OptionalString(@params, "baseRevision");
		var itemId       = OptionalItemId(@params, node);
		var questions    = TdcpQuestionAnswerParams.Optional(@params);
		if (questions != null && itemId == null)
			throw new TdcpInvalidParamsException("'questions' needs an 'itemId'");
		CheckQuestionShape(questions);

		// The compare and the write are one step inside the draft service (V6), so a save of the desktop composer cannot slip in between.
		// An empty draft is no draft (the Avalonia composer deletes it too): an empty text deletes (a Focus draft with question values is not empty).
		bool written;
		string revision;
		if (itemId == null)
		{
			written  = _drafts.TryWriteDraft(node.FileName, text, current => baseRevision == null || baseRevision == _revisions.Of(current));
			revision = _revisions.Of(text);
		}
		else
		{
			written  = _drafts.TryWriteItemDraftFull(node.FileName, itemId, text, questions, current => baseRevision == null || baseRevision == _revisions.Of(current));
			revision = _revisions.Of(new ItemDraftModel(text, questions));
		}

		if (!written)
			throw new TdcpException(Constants.Composer.DraftChangedMessage);

		return new DraftSetResult(revision);
	}

	private object? Delete(JsonNode? @params)
	{
		var node   = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));
		var itemId = OptionalItemId(@params, node);

		var existed = false;
		Func<string?, bool> note = current =>
		{
			existed = current != null;
			return true;
		};
		if (itemId == null)
			_drafts.TryWriteDraft(node.FileName, string.Empty, note);
		else
			_drafts.TryWriteItemDraftFull(node.FileName, itemId, string.Empty, null, current =>
			{
				existed = current != null;
				return true;
			});
		return new DraftDeleteResult(existed);
	}

	// The values are not checked against the item's questionnaire here (a save in flight may meet a re-post, FR.23.37: the client drops what no longer fits);
	// only their shape is, so a draft file never holds something the reply could not read back.
	private static void CheckQuestionShape(IReadOnlyList<AttentionQuestionAnswerParams>? questions)
	{
		if (questions == null)
			return;

		var seen = new HashSet<int>();
		foreach (var question in questions)
		{
			if (question.Number < 1 || question.Number > Constants.Attention.MaxQuestionsPerItem)
				throw new TdcpInvalidParamsException($"question number {question.Number} is out of range (1 to {Constants.Attention.MaxQuestionsPerItem})");
			if (!seen.Add(question.Number))
				throw new TdcpInvalidParamsException($"question number {question.Number} appears more than once");
		}
	}

	// The item id becomes part of a file name, so only the characters of an item id (A12) are accepted; and the item must be an open one of the session.
	private string? OptionalItemId(JsonNode? @params, SessionNodeModel node)
	{
		var itemId = TdcpParams.OptionalString(@params, "itemId");
		if (itemId == null)
			return null;

		if (!DraftService.IsValidItemId(itemId))
			throw new TdcpInvalidParamsException($"'itemId' must be 1 to {Constants.MaxItemIdLength} letters, digits, '_' or '-'");

		var item = _attention?.GetItems(node.NodeId).FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
		if (_attention != null && (item == null || item.Status != AttentionStatus.Open))
			throw new TdcpInvalidParamsException("'itemId' is not an open item of the session");
		return itemId;
	}
}
