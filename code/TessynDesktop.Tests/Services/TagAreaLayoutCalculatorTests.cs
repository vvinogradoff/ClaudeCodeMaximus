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

	[Fact]
	public void LineHeight_IsTheTallestChipOfTheFirstLine()
	{
		var m = _calc.Compute([new TagChipBox(0, 20), new TagChipBox(0, 28), new TagChipBox(28, 28)], expanded: false);

		Assert.Equal(28, m.LineHeight);
		Assert.Equal(1, m.HiddenCount);
	}
}
