namespace TessynDesktop.WPF.Services;

/// <summary>Inserts text into the prompt input at a caret or over a selection; clamps a selection that no longer fits the text.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ComposerTextInserter
{
	/// <summary>Replaces <paramref name="length"/> characters at <paramref name="start"/> with <paramref name="insertion"/>; the caret lands after it.</summary>
	public ComposerInsertion Insert(string text, int start, int length, string insertion)
	{
		start = Math.Clamp(start, 0, text.Length);
		length = Math.Clamp(length, 0, text.Length - start);
		return new ComposerInsertion(string.Concat(text.AsSpan(0, start), insertion, text.AsSpan(start + length)), start + insertion.Length);
	}
}
