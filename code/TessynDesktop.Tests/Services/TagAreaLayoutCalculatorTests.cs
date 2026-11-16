using System.Collections.Generic;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>Line and "+N" counting of the tag area (FR.24.16).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagAreaLayoutCalculatorTests
{
	private const double Line = 24;
	private readonly TagAreaLayoutCalculator _calc = new();

	/// <summary>Chips laid out per line: the number of chips on each line.</summary>
	private static List<TagChipBox> Lines(params int[] chipsPerLine)
	{
		var boxes = new List<TagChipBox>();
		for (var line = 0; line < chipsPerLine.Length; line++)
			for (var i = 0; i < chipsPerLine[line]; i++)
				boxes.Add(new TagChipBox(line * Line, Line));
		return boxes;
	}

	[Fact]
	public void NoChips_LeavesTheHeightFree()
	{
		var m = _calc.Compute([], expanded: false);

		Assert.Equal(new TagAreaMetrics(0, null, 0, false), m);
	}

	[Fact]
	public void Mini_ShowsOneLine_AndCountsTheChipsBelowIt()
	{
		var m = _calc.Compute(Lines(3, 2, 4), expanded: false);

		Assert.Equal(Line, m.AreaHeight);
		Assert.Equal(6, m.HiddenCount);
		Assert.False(m.Scroll);
	}

	[Fact]
	public void Mini_WithEverythingOnTheFirstLine_HidesNothing()
	{
		var m = _calc.Compute(Lines(5), expanded: false);

		Assert.Equal(Line, m.AreaHeight);
		Assert.Equal(0, m.HiddenCount);
	}

	[Fact]
	public void Full_GrowsToFitTheChips()
	{
		var m = _calc.Compute(Lines(2, 2, 1), expanded: true);

		Assert.Equal(3 * Line, m.AreaHeight);
		Assert.Equal(0, m.HiddenCount);
		Assert.True(m.Scroll);
	}

	[Fact]
	public void Full_StopsAtTenLines_ThenScrolls()
	{
		var m = _calc.Compute(Lines(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1), expanded: true);

		Assert.Equal(Constants.TagArea.MaxFullLines * Line, m.AreaHeight);
		Assert.True(m.Scroll);
	}

	[Fact]
	public void Full_WithExactlyTenLines_FitsWithoutCutting()
	{
		var m = _calc.Compute(Lines(1, 1, 1, 1, 1, 1, 1, 1, 1, 1), expanded: true);

		Assert.Equal(10 * Line, m.AreaHeight);
	}

	[Fact]
	public void ChipsNotMeasuredYet_LeaveTheHeightFree()
	{
		var m = _calc.Compute([new TagChipBox(0, 0), new TagChipBox(0, 0)], expanded: false);

		Assert.Null(m.AreaHeight);
		Assert.Equal(0, m.HiddenCount);
	}

	[Fact]
	public void LayoutRounding_DoesNotMoveAChipToTheNextLine()
	{
		// A chip a fraction below the line top, and one a fraction above the second line, belong to their lines.
		var m = _calc.Compute([new TagChipBox(0.2, Line), new TagChipBox(Line - 0.2, Line)], expanded: false);

		Assert.Equal(1, m.HiddenCount);
	}

	/// <summary>Chips 20 high with a 4 px margin between lines: the pitch is 24 and the chips sit 24 apart.</summary>
	private static List<TagChipBox> MarginLines(int lines)
	{
		var boxes = new List<TagChipBox>();
		for (var line = 0; line < lines; line++)
			boxes.Add(new TagChipBox(line * 24, 20, 50));
		return boxes;
	}

	[Fact]
	public void Full_UsesTheLinePitchIncludingTheMargin_SoTenLinesReallyFit()
	{
		var m = _calc.Compute(MarginLines(14), expanded: true);

		Assert.Equal(24, m.LineHeight);
		Assert.Equal(10 * 24, m.AreaHeight);
	}

	[Fact]
	public void Full_WithFewLines_StopsAtTheLastChip()
	{
		var m = _calc.Compute(MarginLines(3), expanded: true);

		Assert.Equal(2 * 24 + 20, m.AreaHeight);
	}

	[Fact]
	public void Mini_UsesThePitchToCountHiddenChips()
	{
		var m = _calc.Compute(MarginLines(4), expanded: false);

		Assert.Equal(24, m.AreaHeight);
		Assert.Equal(3, m.HiddenCount);
	}

	[Fact]
	public void TheHint_IsDropped_WhenTheChipsFitTheLineWithoutItsReserve()
	{
		// Two 50 px chips: the second was pushed down by a 20 px reserve on a 110 px line, but fits in the 130 px the line has without it.
		var chips = new List<TagChipBox> { new(0, 20, 50), new(24, 20, 50) };

		Assert.Equal(1, _calc.Compute(chips, expanded: false).HiddenCount);
		Assert.Equal(1, _calc.Compute(chips, expanded: false, unreservedWidth: 90).HiddenCount);
		Assert.Equal(0, _calc.Compute(chips, expanded: false, unreservedWidth: 130).HiddenCount);
	}

	[Fact]
	public void TheHint_StaysWhenTheChipsDoNotFitEvenWithoutTheReserve()
	{
		var chips = new List<TagChipBox> { new(0, 20, 60), new(24, 20, 60) };

		Assert.Equal(1, _calc.Compute(chips, expanded: false, unreservedWidth: 100).HiddenCount);
	}

	[Fact]
	public void LineHeight_IsTheTallestChipOfTheFirstLine()
	{
		var m = _calc.Compute([new TagChipBox(0, 20), new TagChipBox(0, 28), new TagChipBox(28, 28)], expanded: false);

		Assert.Equal(28, m.LineHeight);
		Assert.Equal(1, m.HiddenCount);
	}
}
