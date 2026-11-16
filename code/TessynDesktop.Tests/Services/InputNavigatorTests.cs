using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>Walk kind, next-input selection, key actions and reveal offsets of input navigation (FR.2.6).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class InputNavigatorTests
{
	private readonly InputNavigator _nav = new();

	private static readonly InputNavItem Other = new(false, false);
	private static readonly InputNavItem Mine = new(true, false);
	private static readonly InputNavItem Auto = new(true, true);

	// 0 other, 1 mine, 2 auto, 3 other, 4 mine, 5 auto, 6 other, 7 mine
	private static readonly InputNavItem[] Items = [Other, Mine, Auto, Other, Mine, Auto, Other, Mine];

	[Fact]
	public void FromMyInput_TheWalkVisitsMyInputsOnly()
	{
		Assert.Equal(4, _nav.FindNext(Items, 1, 1, allInputs: false));
		Assert.Equal(7, _nav.FindNext(Items, 4, 1, allInputs: false));
		Assert.Equal(4, _nav.FindNext(Items, 7, -1, allInputs: false));
		Assert.Equal(1, _nav.FindNext(Items, 4, -1, allInputs: false));
	}

	[Fact]
	public void FromAnAutomatedInput_TheWalkVisitsEveryInput()
	{
		Assert.Equal(4, _nav.FindNext(Items, 2, 1, allInputs: true));
		Assert.Equal(5, _nav.FindNext(Items, 4, 1, allInputs: true));
		Assert.Equal(2, _nav.FindNext(Items, 4, -1, allInputs: true));
		Assert.Equal(1, _nav.FindNext(Items, 2, -1, allInputs: true));
	}

	[Fact]
	public void NoFurtherInputThatWay_ReturnsMinusOne()
	{
		Assert.Equal(-1, _nav.FindNext(Items, 7, 1, allInputs: true));
		Assert.Equal(-1, _nav.FindNext(Items, 1, -1, allInputs: true));
		Assert.Equal(-1, _nav.FindNext(Items, 1, -1, allInputs: false));
	}

	[Fact]
	public void MyOnlyWalk_StepsOverAutomatedInputs_AndAllInputsDoesNot()
	{
		Assert.Equal(4, _nav.FindNext(Items, 1, 1, allInputs: false));
		Assert.Equal(2, _nav.FindNext(Items, 1, 1, allInputs: true));
	}

	[Fact]
	public void FromARowThatIsNotAnInput_TheNearestInputIsFound()
	{
		Assert.Equal(1, _nav.FindNext(Items, 0, 1, allInputs: false));
		Assert.Equal(4, _nav.FindNext(Items, 3, 1, allInputs: false));
	}

	[Fact]
	public void AnEmptyList_HasNoNextInput() =>
		Assert.Equal(-1, _nav.FindNext([], 0, 1, allInputs: true));

	[Fact]
	public void UpDown_WithASelection_Navigate()
	{
		Assert.Equal(new InputKeyAction(InputKeyActionKind.Navigate, -1), _nav.KeyAction(InputNavKey.Up, false, true, 600));
		Assert.Equal(new InputKeyAction(InputKeyActionKind.Navigate, 1), _nav.KeyAction(InputNavKey.Down, false, true, 600));
	}

	[Fact]
	public void UpDown_WithoutASelection_ScrollByALine()
	{
		Assert.Equal(new InputKeyAction(InputKeyActionKind.Scroll, 0, -Constants.InputNav.LineStepPx), _nav.KeyAction(InputNavKey.Up, false, false, 600));
		Assert.Equal(new InputKeyAction(InputKeyActionKind.Scroll, 0, Constants.InputNav.LineStepPx), _nav.KeyAction(InputNavKey.Down, false, false, 600));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void PageKeys_ScrollByAPage_WhateverTheSelection(bool selected)
	{
		var up = _nav.KeyAction(InputNavKey.PageUp, false, selected, 800);
		var down = _nav.KeyAction(InputNavKey.PageDown, false, selected, 800);

		Assert.Equal(InputKeyActionKind.Scroll, up.Kind);
		Assert.Equal(-700, up.ScrollDelta);
		Assert.Equal(700, down.ScrollDelta);
	}

	[Fact]
	public void ATinyViewport_StillPagesByAtLeastALine() =>
		Assert.Equal(Constants.InputNav.LineStepPx, _nav.KeyAction(InputNavKey.PageDown, false, false, 10).ScrollDelta);

	[Fact]
	public void Escape_ClearsOnlyWhileSomethingIsSelected()
	{
		Assert.Equal(InputKeyActionKind.Clear, _nav.KeyAction(InputNavKey.Escape, false, true, 600).Kind);
		Assert.Equal(InputKeyActionKind.None, _nav.KeyAction(InputNavKey.Escape, false, false, 600).Kind);
	}

	[Theory]
	[InlineData(InputNavKey.Up)]
	[InlineData(InputNavKey.Down)]
	[InlineData(InputNavKey.PageUp)]
	[InlineData(InputNavKey.PageDown)]
	[InlineData(InputNavKey.Escape)]
	public void KeysWithAModifier_AreLeftAlone(InputNavKey key) =>
		Assert.Equal(InputKeyActionKind.None, _nav.KeyAction(key, true, true, 600).Kind);

	[Fact]
	public void AnEntryFullyInView_DoesNotScroll() =>
		Assert.Equal(100, _nav.OffsetToReveal(100, 500, 150, 100, 8));

	[Fact]
	public void AnEntryAboveTheView_IsAlignedNearTheTop() =>
		Assert.Equal(192, _nav.OffsetToReveal(300, 500, 200, 100, 8));

	[Fact]
	public void AnEntryBelowTheView_IsScrolledJustIntoView() =>
		// bottom 800 + 100 + 8 = 908 must sit at the bottom edge of a 500 px view.
		Assert.Equal(408, _nav.OffsetToReveal(0, 500, 800, 100, 8));

	[Fact]
	public void AnEntryTallerThanTheView_IsAlignedToItsTop() =>
		Assert.Equal(992, _nav.OffsetToReveal(0, 500, 1000, 900, 8));

	[Fact]
	public void TheOffset_IsNeverNegative() =>
		Assert.Equal(0, _nav.OffsetToReveal(50, 500, 3, 100, 8));

	[Fact]
	public void AClickWithoutADrag_Selects_AndADragOrATextSelectionDoesNot()
	{
		Assert.True(_nav.IsSelectingClick(0, false));
		Assert.True(_nav.IsSelectingClick(Constants.InputNav.ClickDragTolerancePx, false));
		Assert.False(_nav.IsSelectingClick(Constants.InputNav.ClickDragTolerancePx + 1, false));
		Assert.False(_nav.IsSelectingClick(0, true));
	}
}
