using System.Windows.Documents;

namespace TessynDesktop.WPF.Views;

/// <summary>A local path inside the text of a Focus card (FR.23.30): wraps the characters of the path, remembers it as written, and carries the dotted underline.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusPathSpan : Span
{
	public FocusPathSpan(TextPointer start, TextPointer end, string path) : base(start, end)
	{
		Path = path;
	}

	/// <summary>The path exactly as the card's text spells it; what <c>file.preview</c> is asked about.</summary>
	public string Path { get; }
}
