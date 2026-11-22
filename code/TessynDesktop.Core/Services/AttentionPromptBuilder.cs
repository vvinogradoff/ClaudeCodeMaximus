using System.Collections.Generic;
using System.Text;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>Builds the one prompt "Send answers" sends from the Focus view (FR.23.15).</summary>
/// <remarks>Created by Claude</remarks>
public static class AttentionPromptBuilder
{
	/// <summary>
	/// <c>Answers from the Focus view:</c> then one <c>- A3 (title): answer</c> line per answer, in input order.
	/// Line breaks inside an answer are kept; continuation lines are indented by two spaces. An empty list
	/// yields an empty string (the UI never sends one).
	/// </summary>
	public static string BuildAnswersPrompt(IReadOnlyList<AttentionAnswerModel> answers)
	{
		if (answers.Count == 0)
			return string.Empty;

		var sb = new StringBuilder(Constants.Attention.AnswersPromptHeader);
		foreach (var answer in answers)
		{
			var text = (answer.StartsWithQuestions ? "\n" : string.Empty) + answer.Answer.Trim().Replace("\r\n", "\n").Replace('\r', '\n');
			text = text.Replace("\n", "\n" + Constants.Attention.AnswersContinuationIndent);
			sb.Append('\n').Append(string.Format(Constants.Attention.AnswersPromptLineFormat, answer.ItemId, answer.Title, text));
		}

		return sb.ToString();
	}
}
