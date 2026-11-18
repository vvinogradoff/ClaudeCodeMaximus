using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace TessynDesktop.WPF.Rendering;

/// <summary>
/// FR.2.12: a line made only of three or more <c>=</c> (spaces allowed) is a double horizontal rule, and a line of three or more <c>-</c> a plain one. With setext headings off it
/// interrupts a paragraph (the stock thematic-break parser does not, because there <c>---</c> would be a setext heading), so the text above stays a paragraph. The block is a thematic break with the
/// class <c>dbl</c>, so it renders as <c>&lt;hr class="dbl"&gt;</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class RuleLineBlockParser : BlockParser
{
	public const string RuleClass = "dbl";
	private const int MinimumLength = 3;

	public RuleLineBlockParser() => OpeningCharacters = ['=', '-'];

	public override BlockState TryOpen(BlockProcessor processor)
	{
		// An indent of 4 or more is a code block, as for any other block.
		if (processor.IsCodeIndent)
			return BlockState.None;

		var slice = processor.Line;
		var marker = slice.CurrentChar;
		var count = 0;
		var c = marker;
		while (c != '\0')
		{
			if (c == marker)
				count++;
			else if (!c.IsSpaceOrTab() || (marker == '-' && count > 0 && count < MinimumLength))
				return BlockState.None;

			c = slice.NextChar();
		}

		if (count < MinimumLength)
			return BlockState.None;

		var block = new ThematicBreakBlock(this)
		{
			Column = processor.Column,
			Span = new SourceSpan(processor.Start, slice.End),
		};
		if (marker == '=')
			block.GetAttributes().AddClass(RuleClass);
		processor.NewBlocks.Push(block);
		return BlockState.BreakDiscard;
	}
}
