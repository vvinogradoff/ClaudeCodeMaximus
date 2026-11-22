using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>Text forms of questionnaire questions shared by the session file, the answers prompt and the notification (FR.23.36, FR.23.38).</summary>
/// <remarks>Created by Claude</remarks>
public static class AttentionQuestionFormat
{
	/// <summary>The letter of choice <paramref name="index"/>: A to D.</summary>
	public static char Letter(int index) => (char)('A' + index);

	/// <summary><c>B Label</c>: a choice with its letter.</summary>
	public static string ChoiceWithLetter(AttentionQuestionModel question, int index) =>
		string.Format(Constants.Attention.AnswerChoiceFormat, Letter(index), question.Choices[index]);

	/// <summary>Index of <paramref name="label"/> among the choices, compared case-insensitively after trimming; -1 when none matches.</summary>
	public static int IndexOfChoice(AttentionQuestionModel question, string? label)
	{
		var wanted = label?.Trim() ?? string.Empty;
		return question.Choices.FindIndex(c => string.Equals(c.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>Joins the lines of a multi-line text onto one line with <see cref="Constants.Attention.MultiLineJoiner"/>.</summary>
	public static string OneLine(string text)
	{
		var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
			.Split('\n')
			.Select(l => l.Trim())
			.Where(l => l.Length > 0);
		return string.Join(Constants.Attention.MultiLineJoiner, lines);
	}

	/// <summary>The session-file line of a question (FR.23.38), e.g. <c>Q2 [single: A x | B y] Which? (recommended: A)</c>.</summary>
	public static string SessionLine(AttentionQuestionModel question)
	{
		var text = OneLine(question.Text);
		var choices = string.Join(Constants.Attention.SystemQuestionChoiceSeparator,
			question.Choices.Select((_, i) => ChoiceWithLetter(question, i)));

		var line = question.Type switch
		{
			AttentionQuestionType.YesNo  => string.Format(Constants.Attention.SystemQuestionYesNoFormat, question.Number, text),
			AttentionQuestionType.Single => string.Format(Constants.Attention.SystemQuestionSingleFormat, question.Number, choices, text),
			AttentionQuestionType.Multi  => string.Format(Constants.Attention.SystemQuestionMultiFormat, question.Number, choices, text),
			_                            => string.Format(Constants.Attention.SystemQuestionTextFormat, question.Number, text),
		};

		var recommended = SessionRecommendation(question);
		return recommended == null ? line : line + string.Format(Constants.Attention.SystemQuestionRecommendedFormat, recommended);
	}

	private static string? SessionRecommendation(AttentionQuestionModel question)
	{
		switch (question.Type)
		{
			case AttentionQuestionType.Multi:
				var letters = question.RecommendedChoices
					.Select(c => IndexOfChoice(question, c))
					.Where(i => i >= 0)
					.OrderBy(i => i)
					.Select(i => Letter(i).ToString())
					.ToList();
				return letters.Count == 0 ? null : string.Join(Constants.Attention.SystemQuestionRecommendedSeparator, letters);

			case AttentionQuestionType.Single:
				var index = IndexOfChoice(question, question.Recommended);
				return index < 0 ? null : Letter(index).ToString();

			case AttentionQuestionType.Text:
				return string.IsNullOrWhiteSpace(question.Recommended) ? null : OneLine(question.Recommended);

			default:
				return string.IsNullOrWhiteSpace(question.Recommended) ? null : question.Recommended.Trim().ToLowerInvariant();
		}
	}
}
