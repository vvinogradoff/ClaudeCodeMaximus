using System.Collections.Generic;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>TC.FV.18 — the "Send answers" prompt (FR.23.15).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionPromptBuilderTests
{
	private static AttentionAnswerModel Answer(string id, string title, string answer) =>
		new() { ItemId = id, Title = title, Answer = answer };

	[Fact]
	public void BuildAnswersPrompt_TwoAnswers_ProducesTheExactFormatInInputOrder()   // TC.FV.18-1
	{
		var prompt = AttentionPromptBuilder.BuildAnswersPrompt([Answer("A3", "t3", "a3"), Answer("A5", "t5", "a5")]);

		Assert.Equal("Answers from the Focus view:\n- A3 (t3): a3\n- A5 (t5): a5", prompt);
	}

	[Theory]
	[InlineData("line1\nline2\nline3")]
	[InlineData("line1\r\nline2\r\nline3")]
	public void BuildAnswersPrompt_MultiLineAnswer_IndentsContinuationLinesByTwoSpaces(string answer)   // TC.FV.18-2
	{
		var prompt = AttentionPromptBuilder.BuildAnswersPrompt([Answer("A3", "t", answer)]);

		Assert.Equal("Answers from the Focus view:\n- A3 (t): line1\n  line2\n  line3", prompt);
	}

	[Fact]
	public void BuildAnswersPrompt_SingleAnswerWithParenthesesInTheTitle_IsRenderedVerbatim()   // TC.FV.18-3
	{
		var prompt = AttentionPromptBuilder.BuildAnswersPrompt([Answer("A1", "Deploy (prod) now?", "yes")]);

		Assert.Equal("Answers from the Focus view:\n- A1 (Deploy (prod) now?): yes", prompt);
	}

	[Fact]
	public void BuildAnswersPrompt_EmptyList_IsEmptyNotAHeaderOnlyPrompt()   // TC.FV.18-4
	{
		Assert.Equal(string.Empty, AttentionPromptBuilder.BuildAnswersPrompt(new List<AttentionAnswerModel>()));
	}
}
