using System;
using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// The part of "Send answers" (FR.23.15) that desktop and web share: the answers prompt is built and the items are
/// marked answered, in that order. Sending the prompt is left to the caller, after this returns, so that the turn's
/// instruction block no longer lists the answered items as pinned.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class AttentionReplyComposer
{
	/// <summary>
	/// Builds the prompt of <paramref name="answers"/> and calls <paramref name="markAnswered"/> with them. Returns an
	/// empty string, marking nothing, when there is nothing to send.
	/// </summary>
	public static string Compose(IReadOnlyList<AttentionAnswerModel> answers, Action<IReadOnlyList<AttentionAnswerModel>> markAnswered)
	{
		var prompt = AttentionPromptBuilder.BuildAnswersPrompt(answers);
		if (string.IsNullOrEmpty(prompt))
			return string.Empty;

		markAnswered(answers);
		return prompt;
	}
}
