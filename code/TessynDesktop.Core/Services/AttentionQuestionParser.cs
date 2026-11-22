using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Reads and validates the <c>questions</c> of one <c>post_attention</c> item (FR.23.34). The first violation throws a <see cref="McpToolException"/>
/// that names the item and the question (<c>items[0].questions[3]: ...</c>), so the whole call posts nothing.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal static class AttentionQuestionParser
{
	/// <summary>The questionnaire of <paramref name="item"/>, or an empty list when it has no <c>questions</c>.</summary>
	/// <param name="item">The item object of the tool input.</param>
	/// <param name="itemIndex">Its position in <c>items</c>, for the messages.</param>
	/// <param name="kind">Its kind: only ask and critical may carry a questionnaire.</param>
	/// <param name="hasOptionsOrRecommendation">Whether the item also sets <c>options</c> or <c>recommendation</c>, which a questionnaire item must not (FR.23.33).</param>
	public static List<AttentionQuestionModel> Parse(JsonObject item, int itemIndex, AttentionKind kind, bool hasOptionsOrRecommendation)
	{
		var node = item["questions"];
		if (node == null)
			return [];

		var where = $"items[{itemIndex}]";
		if (node is not JsonArray array)
			throw new McpToolException($"{where}: 'questions' must be an array.");
		if (array.Count < 1 || array.Count > Constants.Attention.MaxQuestionsPerItem)
			throw new McpToolException($"{where}: 'questions' must contain 1 to {Constants.Attention.MaxQuestionsPerItem} questions (got {array.Count}).");
		if (kind is not (AttentionKind.Ask or AttentionKind.Critical))
			throw new McpToolException($"{where}: 'questions' are only allowed on ask and critical items (this one is {AttentionFormat.KindName(kind)}).");
		if (hasOptionsOrRecommendation)
			throw new McpToolException($"{where}: an item with 'questions' must not set 'options' or 'recommendation' - put each recommendation on its question.");

		var questions = new List<AttentionQuestionModel>(array.Count);
		for (var q = 0; q < array.Count; q++)
		{
			if (array[q] is not JsonObject obj)
				throw new McpToolException($"{where}.questions[{q}]: must be an object.");

			questions.Add(ParseOne(obj, $"{where}.questions[{q}]", q + 1));
		}

		return questions;
	}

	private static AttentionQuestionModel ParseOne(JsonObject obj, string where, int number)
	{
		var typeText = ReadString(obj, "type", where)?.Trim();
		if (string.IsNullOrEmpty(typeText))
			throw new McpToolException($"{where}: 'type' is required ({AllowedTypes}).");
		if (!TryParseType(typeText, out var type))
			throw new McpToolException($"{where}: invalid type '{typeText}' - use {AllowedTypes}.");

		var text = ReadString(obj, "question", where)?.Trim();
		if (string.IsNullOrEmpty(text))
			throw new McpToolException($"{where}: 'question' is required and must not be empty.");
		if (text.Length > Constants.Attention.MaxQuestionTextLength)
			throw new McpToolException($"{where}: 'question' must be at most {Constants.Attention.MaxQuestionTextLength} characters (got {text.Length}).");

		var hasChoices = type is AttentionQuestionType.Single or AttentionQuestionType.Multi;
		var question   = new AttentionQuestionModel { Number = number, Type = type, Text = text };

		if (hasChoices)
			question.Choices = ParseChoices(obj, where);
		else if (obj["choices"] != null)
			throw new McpToolException($"{where}: 'choices' is only allowed for single and multi questions.");

		var recommended = ReadString(obj, "recommended", where)?.Trim();
		if (type == AttentionQuestionType.Multi)
		{
			if (obj["recommended"] != null)
				throw new McpToolException($"{where}: 'recommended' is not allowed for a multi question - use 'recommended_choices'.");
			question.RecommendedChoices = ParseRecommendedChoices(obj, where, question.Choices);
			return question;
		}

		if (obj["recommended_choices"] != null)
			throw new McpToolException($"{where}: 'recommended_choices' is only allowed for multi questions.");

		question.Recommended = type switch
		{
			AttentionQuestionType.YesNo  => ParseYesNoRecommendation(recommended, where),
			AttentionQuestionType.Single => ParseSingleRecommendation(recommended, where, question.Choices),
			_                            => ParseTextRecommendation(recommended, where),
		};
		return question;
	}

	private static List<string> ParseChoices(JsonObject obj, string where)
	{
		if (obj["choices"] is not JsonArray array)
			throw new McpToolException($"{where}: 'choices' is required for single and multi questions (an array of {Constants.Attention.MinQuestionChoices}-{Constants.Attention.MaxQuestionChoices} labels).");

		var choices = new List<string>(array.Count);
		foreach (var element in array)
		{
			if (element is not JsonValue value || !value.TryGetValue<string>(out var label))
				throw new McpToolException($"{where}: 'choices' must be an array of strings.");

			label = label.Trim();
			if (label.Length == 0)
				throw new McpToolException($"{where}: a choice must not be empty.");
			if (label.Length > Constants.Attention.MaxQuestionChoiceLength)
				throw new McpToolException($"{where}: a choice must be at most {Constants.Attention.MaxQuestionChoiceLength} characters.");
			if (choices.Any(c => string.Equals(c, label, StringComparison.OrdinalIgnoreCase)))
				throw new McpToolException($"{where}: choices must be unique (duplicate '{label}').");

			choices.Add(label);
		}

		if (choices.Count < Constants.Attention.MinQuestionChoices || choices.Count > Constants.Attention.MaxQuestionChoices)
			throw new McpToolException($"{where}: 'choices' must contain {Constants.Attention.MinQuestionChoices} to {Constants.Attention.MaxQuestionChoices} labels (got {choices.Count}).");

		return choices;
	}

	private static List<string> ParseRecommendedChoices(JsonObject obj, string where, List<string> choices)
	{
		if (obj["recommended_choices"] == null)
			return [];
		if (obj["recommended_choices"] is not JsonArray array)
			throw new McpToolException($"{where}: 'recommended_choices' must be an array of strings.");

		var picked = new SortedSet<int>();
		foreach (var element in array)
		{
			if (element is not JsonValue value || !value.TryGetValue<string>(out var label))
				throw new McpToolException($"{where}: 'recommended_choices' must be an array of strings.");

			var index = choices.FindIndex(c => string.Equals(c, label.Trim(), StringComparison.OrdinalIgnoreCase));
			if (index < 0)
				throw new McpToolException($"{where}: recommended choice '{label}' is not one of the choices.");

			picked.Add(index);
		}

		return picked.Select(i => choices[i]).ToList();
	}

	private static string? ParseYesNoRecommendation(string? recommended, string where)
	{
		if (string.IsNullOrEmpty(recommended))
			return null;

		var lower = recommended.ToLowerInvariant();
		if (lower is not (Constants.Attention.AnswerYes or Constants.Attention.AnswerNo))
			throw new McpToolException($"{where}: 'recommended' of a yes_no question must be \"yes\" or \"no\".");

		return lower;
	}

	private static string? ParseSingleRecommendation(string? recommended, string where, List<string> choices)
	{
		if (string.IsNullOrEmpty(recommended))
			return null;

		var index = choices.FindIndex(c => string.Equals(c, recommended, StringComparison.OrdinalIgnoreCase));
		if (index < 0)
			throw new McpToolException($"{where}: 'recommended' must be one of the choices.");

		return choices[index];
	}

	private static string? ParseTextRecommendation(string? recommended, string where)
	{
		if (string.IsNullOrEmpty(recommended))
			return null;
		if (recommended.Length > Constants.Attention.MaxQuestionRecommendationLength)
			throw new McpToolException($"{where}: 'recommended' must be at most {Constants.Attention.MaxQuestionRecommendationLength} characters (got {recommended.Length}).");

		return recommended;
	}

	private static string? ReadString(JsonObject obj, string name, string where)
	{
		var node = obj[name];
		if (node == null)
			return null;
		if (node is JsonValue value && value.TryGetValue<string>(out var text))
			return text;

		throw new McpToolException($"{where}: '{name}' must be a string.");
	}

	private static bool TryParseType(string text, out AttentionQuestionType type)
	{
		switch (text.ToLowerInvariant())
		{
			case Constants.Attention.QuestionTypeYesNo:  type = AttentionQuestionType.YesNo; return true;
			case Constants.Attention.QuestionTypeSingle: type = AttentionQuestionType.Single; return true;
			case Constants.Attention.QuestionTypeMulti:  type = AttentionQuestionType.Multi; return true;
			case Constants.Attention.QuestionTypeText:   type = AttentionQuestionType.Text; return true;
			default:                                     type = default; return false;
		}
	}

	private static string AllowedTypes =>
		string.Join(" | ", Constants.Attention.QuestionTypeYesNo, Constants.Attention.QuestionTypeSingle, Constants.Attention.QuestionTypeMulti, Constants.Attention.QuestionTypeText);
}
