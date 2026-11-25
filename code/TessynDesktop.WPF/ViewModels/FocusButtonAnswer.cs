namespace TessynDesktop.WPF.ViewModels;

/// <summary>The reply box after an answer button was clicked, and the button text to remember for the next click (null: the user's text, append from now on).</summary>
public sealed record FocusButtonAnswerResult(string Text, string? LastButtonText);

/// <summary>
/// The rule for Focus answer buttons (FR.23.44), the same as the web's <c>applyButtonAnswer</c>: a button replaces the reply box when it is blank or still holds exactly
/// what the last button put there; once the user has typed or edited it, the button's text is appended on a new line and every later click appends too.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class FocusButtonAnswer
{
	public static FocusButtonAnswerResult Apply(string current, string? lastButtonText, string addition)
	{
		if (string.IsNullOrWhiteSpace(current) || string.Equals(current, lastButtonText, StringComparison.Ordinal))
			return new FocusButtonAnswerResult(addition, addition);

		return new FocusButtonAnswerResult(current.TrimEnd() + "\n" + addition, null);
	}
}
