using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.23.44: the answer-button rule, with the same cases as the web's <c>applyButtonAnswer</c> tests (lib.test.ts).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusButtonAnswerTests
{
	private static FocusButtonAnswerResult Click(FocusButtonAnswerResult state, string answer) => FocusButtonAnswer.Apply(state.Text, state.LastButtonText, answer);

	private static readonly FocusButtonAnswerResult Empty = new(string.Empty, null);

	[Fact]
	public void FillsAnEmptyReply() => Assert.Equal("Yes", FocusButtonAnswer.Apply("  ", null, "Yes").Text);

	[Fact]
	public void SwitchesCleanlyBetweenUntouchedButtonAnswers()
	{
		var s = Click(Empty, "Variant A");
		s = Click(s, "Something else (say)");
		s = Click(s, "Variant B");

		Assert.Equal("Variant B", s.Text);
		Assert.Equal("Variant B", s.LastButtonText);
	}

	[Fact]
	public void AppendsOnceTheUserHasEditedTheText_AndKeepsAppending()
	{
		var s = Click(Empty, "Variant A");
		s = Click(s with { Text = "Variant A as it looks better" }, "Something else (say)");
		Assert.Null(s.LastButtonText);
		s = Click(s, "Variant B");

		Assert.Equal("Variant A as it looks better\nSomething else (say)\nVariant B", s.Text);
	}

	[Fact]
	public void NeverOverwritesTypedText() => Assert.Equal("Go ahead, but\nOption B", FocusButtonAnswer.Apply("Go ahead, but\n", null, "Option B").Text);
}
