using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The one formatter of questionnaire answers (FR.23.36), used for every client: it checks the values against the item's questionnaire and builds the
/// item's entry of the answers prompt, e.g. <c>Q2 Which?: B Beta (changed; recommended: A Alpha)</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class AttentionAnswerFormatter
{
	/// <summary>
	/// Checks <paramref name="answers"/> against the questionnaire of <paramref name="item"/>. A number out of range or repeated, an unknown choice, or a value
	/// of the wrong shape throws <see cref="TdcpInvalidParamsException"/> (-32602). Null or empty answers are valid for every item.
	/// </summary>
	public static void Validate(AttentionItemModel item, IReadOnlyList<AttentionQuestionAnswerParams>? answers)
	{
		if (answers == null || answers.Count == 0)
			return;

		if (item.Questions.Count == 0)
			throw new TdcpInvalidParamsException($"{item.Id} has no questionnaire, so 'questions' is not allowed");

		var seen = new HashSet<int>();
		foreach (var answer in answers)
		{
			var question = item.Questions.FirstOrDefault(q => q.Number == answer.Number)
				?? throw new TdcpInvalidParamsException($"{item.Id}: question number {answer.Number} is out of range (1 to {item.Questions.Count})");
			if (!seen.Add(answer.Number))
				throw new TdcpInvalidParamsException($"{item.Id} Q{answer.Number}: answered more than once");

			ValidateOne(item.Id, question, answer);
		}
	}

	/// <summary>
	/// The answer of <paramref name="item"/> as the answers prompt shows it (FR.23.36): the <paramref name="note"/>, then one line per question. Questions that
	/// <paramref name="answers"/> does not mention read as unanswered. <paramref name="answers"/> must have passed <see cref="Validate"/>.
	/// </summary>
	public static AttentionAnswerModel Compose(AttentionItemModel item, string note, IReadOnlyList<AttentionQuestionAnswerParams>? answers)
	{
		var lines = item.Questions
			.OrderBy(q => q.Number)
			.Select(q => FormatLine(q, answers?.FirstOrDefault(a => a.Number == q.Number)))
			.ToList();

		var trimmed = note.Trim();
		var text = trimmed.Length == 0
			? string.Join("\n", lines)
			: string.Join("\n", new[] { trimmed }.Concat(lines));

		return new AttentionAnswerModel
		{
			ItemId              = item.Id,
			Title               = item.Title,
			Answer              = text,
			StartsWithQuestions = trimmed.Length == 0 && lines.Count > 0,
		};
	}

	/// <summary>One question line: <c>Q1 text: value (mark)</c>.</summary>
	public static string FormatLine(AttentionQuestionModel question, AttentionQuestionAnswerParams? answer)
	{
		var (value, mark) = question.Type switch
		{
			AttentionQuestionType.YesNo  => FormatYesNo(question, answer),
			AttentionQuestionType.Single => FormatSingle(question, answer),
			AttentionQuestionType.Multi  => FormatMulti(question, answer),
			_                            => FormatText(question, answer),
		};

		return string.Format(Constants.Attention.AnswerQuestionLineFormat, question.Number, AttentionQuestionFormat.OneLine(question.Text), value + mark);
	}

	private static (string Value, string Mark) FormatYesNo(AttentionQuestionModel question, AttentionQuestionAnswerParams? answer)
	{
		var current     = YesNoOf(answer?.Value);
		var recommended = YesNoOf(question.Recommended);
		var value       = current == null ? Constants.Attention.AnswerNoAnswer : YesNoLabel(current.Value);

		if (recommended == null)
			return (value, string.Empty);

		return current == recommended
			? (value, Constants.Attention.AnswerAsRecommended)
			: (value, string.Format(Constants.Attention.AnswerChangedFormat, YesNoLabel(recommended.Value)));
	}

	private static (string Value, string Mark) FormatSingle(AttentionQuestionModel question, AttentionQuestionAnswerParams? answer)
	{
		var current     = AttentionQuestionFormat.IndexOfChoice(question, answer?.Value);
		var recommended = AttentionQuestionFormat.IndexOfChoice(question, question.Recommended);
		var value       = current < 0 ? Constants.Attention.AnswerNoAnswer : AttentionQuestionFormat.ChoiceWithLetter(question, current);

		if (recommended < 0)
			return (value, string.Empty);

		return current == recommended
			? (value, Constants.Attention.AnswerAsRecommended)
			: (value, string.Format(Constants.Attention.AnswerChangedFormat, AttentionQuestionFormat.ChoiceWithLetter(question, recommended)));
	}

	private static (string Value, string Mark) FormatMulti(AttentionQuestionModel question, AttentionQuestionAnswerParams? answer)
	{
		var current     = IndexesOf(question, answer?.Choices);
		var recommended = IndexesOf(question, question.RecommendedChoices);
		var value       = current.Count == 0 ? Constants.Attention.AnswerNoneSelected : JoinChoices(question, current);

		if (recommended.Count == 0)
			return (value, string.Empty);

		return current.SequenceEqual(recommended)
			? (value, Constants.Attention.AnswerAsRecommended)
			: (value, string.Format(Constants.Attention.AnswerChangedFormat, JoinChoices(question, recommended)));
	}

	private static (string Value, string Mark) FormatText(AttentionQuestionModel question, AttentionQuestionAnswerParams? answer)
	{
		var current     = AttentionQuestionFormat.OneLine(answer?.Value ?? string.Empty);
		var recommended = AttentionQuestionFormat.OneLine(question.Recommended ?? string.Empty);
		var value       = current.Length == 0 ? Constants.Attention.AnswerNoAnswer : current;

		if (recommended.Length == 0)
			return (value, string.Empty);

		return string.Equals(current, recommended, StringComparison.Ordinal)
			? (value, Constants.Attention.AnswerAsRecommended)
			: (value, Constants.Attention.AnswerChangedText);
	}

	private static void ValidateOne(string itemId, AttentionQuestionModel question, AttentionQuestionAnswerParams answer)
	{
		var where = $"{itemId} Q{question.Number}";
		var hasValue = !string.IsNullOrWhiteSpace(answer.Value);

		switch (question.Type)
		{
			case AttentionQuestionType.Multi:
				if (answer.Value != null)
					throw new TdcpInvalidParamsException($"{where}: a multi question takes 'choices', not 'value'");
				foreach (var label in answer.Choices ?? [])
				{
					if (AttentionQuestionFormat.IndexOfChoice(question, label) < 0)
						throw new TdcpInvalidParamsException($"{where}: '{label}' is not one of the choices");
				}
				return;

			case AttentionQuestionType.YesNo:
				RejectChoices(where, answer);
				if (hasValue && YesNoOf(answer.Value) == null)
					throw new TdcpInvalidParamsException($"{where}: 'value' must be \"yes\" or \"no\"");
				return;

			case AttentionQuestionType.Single:
				RejectChoices(where, answer);
				if (hasValue && AttentionQuestionFormat.IndexOfChoice(question, answer.Value) < 0)
					throw new TdcpInvalidParamsException($"{where}: '{answer.Value}' is not one of the choices");
				return;

			default:
				RejectChoices(where, answer);
				return;
		}
	}

	private static void RejectChoices(string where, AttentionQuestionAnswerParams answer)
	{
		if (answer.Choices != null)
			throw new TdcpInvalidParamsException($"{where}: 'choices' is for multi questions only");
	}

	private static bool? YesNoOf(string? value) => value?.Trim().ToLowerInvariant() switch
	{
		Constants.Attention.AnswerYes => true,
		Constants.Attention.AnswerNo  => false,
		_                             => null,
	};

	private static string YesNoLabel(bool yes) => yes ? Constants.Attention.AnswerYesLabel : Constants.Attention.AnswerNoLabel;

	/// <summary>The distinct choice indexes of <paramref name="labels"/>, ascending; labels that match nothing are ignored.</summary>
	private static List<int> IndexesOf(AttentionQuestionModel question, IEnumerable<string>? labels) =>
		(labels ?? []).Select(l => AttentionQuestionFormat.IndexOfChoice(question, l)).Where(i => i >= 0).Distinct().OrderBy(i => i).ToList();

	private static string JoinChoices(AttentionQuestionModel question, IEnumerable<int> indexes) =>
		string.Join(Constants.Attention.AnswerChoiceSeparator, indexes.Select(i => AttentionQuestionFormat.ChoiceWithLetter(question, i)));
}
