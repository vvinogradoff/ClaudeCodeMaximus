using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IAttentionReplyService" />
/// <remarks>Created by Claude</remarks>
public sealed class AttentionReplyService : IAttentionReplyService
{
	private readonly IAttentionService _attention;
	private readonly ISessionSendService _send;

	public AttentionReplyService(IAttentionService attention, ISessionSendService send)
	{
		_attention = attention;
		_send      = send;
	}

	/// <inheritdoc />
	public AttentionReplyResult Reply(SessionNodeModel node, IReadOnlyList<AttentionAnswerParams> answers, TurnOptionsModel? options)
	{
		var openItems = _attention.GetItems(node.NodeId)
			.Where(item => item.Status == AttentionStatus.Open)
			.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

		var accepted = new List<AttentionAnswerModel>();
		var skipped  = new List<string>();
		var seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var valid    = new List<(AttentionItemModel Item, string Text, IReadOnlyList<AttentionQuestionAnswerParams>? Questions)>();

		foreach (var answer in answers)
		{
			var itemId = answer.ItemId.Trim();
			var text   = answer.Text.Trim();

			if (!openItems.TryGetValue(itemId, out var item) || !seen.Add(item.Id))
			{
				skipped.Add(itemId);
				continue;
			}

			// FR.23.36: a questionnaire item's answers are its reply, so an empty note is fine for it. The values of every answer are
			// checked before anything is marked or sent, so a bad one rejects the whole call.
			if (item.Questions.Count == 0 && text.Length == 0)
			{
				skipped.Add(itemId);
				continue;
			}

			AttentionAnswerFormatter.Validate(item, answer.Questions);
			valid.Add((item, text, answer.Questions));
		}

		foreach (var (item, text, questions) in valid)
		{
			accepted.Add(item.Questions.Count > 0
				? AttentionAnswerFormatter.Compose(item, text, questions)
				: new AttentionAnswerModel { ItemId = item.Id, Title = item.Title, Answer = text });
		}

		var prompt = AttentionReplyComposer.Compose(accepted, marked => _attention.MarkAnsweredByUser(node.NodeId, marked));
		if (prompt.Length == 0)
			return new AttentionReplyResult([], skipped, TurnStarted: false);

		_send.Send(node, prompt, options);
		return new AttentionReplyResult(accepted.Select(a => a.ItemId).ToList(), skipped, TurnStarted: true);
	}
}
