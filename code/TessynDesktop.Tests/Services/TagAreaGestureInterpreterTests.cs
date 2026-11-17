using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>The click gestures of the tag area (FR.24.16).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagAreaGestureInterpreterTests
{
	private readonly TagAreaGestureInterpreter _g = new();

	private TagAreaGestureAction Press(int clicks, bool onChip = false, bool onScrollBar = false, bool expanded = false, bool hasTags = true) =>
		_g.OnPress(clicks, onChip, onScrollBar, expanded, hasTags);

	[Fact]
	public void ClickOnEmptySpaceOrTheHint_InMini_Expands() =>
		Assert.Equal(TagAreaGestureAction.Expand, Press(1));

	[Fact]
	public void ClickOnAChip_DoesNotChangeTheMode() =>
		Assert.Equal(TagAreaGestureAction.None, Press(1, onChip: true));

	[Fact]
	public void ClickOnEmptySpace_InFull_DoesNothing() =>
		Assert.Equal(TagAreaGestureAction.None, Press(1, expanded: true));

	[Fact]
	public void DoubleClick_InFull_CollapsesOnEmptySpace()
	{
		Assert.Equal(TagAreaGestureAction.None, Press(1, expanded: true));
		Assert.Equal(TagAreaGestureAction.Collapse, Press(2, expanded: true));
	}

	[Fact]
	public void DoubleClick_OnAChip_InFull_DoesNotCollapse()
	{
		Assert.Equal(TagAreaGestureAction.None, Press(1, onChip: true, expanded: true));
		Assert.Equal(TagAreaGestureAction.None, Press(2, onChip: true, expanded: true));
	}

	[Fact]
	public void DoubleClick_OnTheScrollBar_NeitherCollapsesNorIsSwallowed()
	{
		Assert.Equal(TagAreaGestureAction.None, Press(1, onScrollBar: true, expanded: true));
		Assert.Equal(TagAreaGestureAction.None, Press(2, onScrollBar: true, expanded: true));
	}

	[Fact]
	public void DoubleClick_OnEmptySpaceInMini_ExpandsOnce_AndTheSecondPressDoesNotFoldIt()
	{
		Assert.Equal(TagAreaGestureAction.Expand, Press(1));
		Assert.Equal(TagAreaGestureAction.Swallow, Press(2, expanded: true));
	}

	[Fact]
	public void TripleClick_OnEmptySpaceInMini_EndsExpanded()
	{
		Press(1);
		Assert.Equal(TagAreaGestureAction.Swallow, Press(2, expanded: true));
		Assert.Equal(TagAreaGestureAction.Swallow, Press(3, expanded: true));
	}

	[Fact]
	public void TheNextClickSequence_CanCollapseAgain()
	{
		Press(1);
		Press(2, expanded: true);

		Assert.Equal(TagAreaGestureAction.None, Press(1, expanded: true));
		Assert.Equal(TagAreaGestureAction.Collapse, Press(2, expanded: true));
	}

	[Fact]
	public void WithoutTags_NothingHappens()
	{
		Assert.Equal(TagAreaGestureAction.None, Press(1, hasTags: false));
		Assert.Equal(TagAreaGestureAction.None, Press(2, expanded: true, hasTags: false));
	}
}
