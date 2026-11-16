using System;
using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// The decisions of input navigation (FR.2.6), UI-free. An input is a USER entry. The walk kind is set by the input the user clicked: an
/// automated input means every input is visited, one of the user's own means only theirs. Nothing selected: Up/Down scroll by a line step and
/// PgUp/PgDn by a page.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class InputNavigator
{
	/// <summary>
	/// Position in <paramref name="items"/> of the nearest input before (<paramref name="direction"/> &lt; 0) or after (&gt; 0) position
	/// <paramref name="from"/>; -1 when there is none. With <paramref name="allInputs"/> false, automated inputs are skipped.
	/// </summary>
	public int FindNext(IReadOnlyList<InputNavItem> items, int from, int direction, bool allInputs)
	{
		var step = direction > 0 ? 1 : -1;
		for (var k = from + step; k >= 0 && k < items.Count; k += step)
		{
			var item = items[k];
			if (item.IsInput && (allInputs || !item.IsAutomated))
				return k;
		}

		return -1;
	}

	/// <summary>
	/// What a key does. Keys pressed with a modifier are left alone (Ctrl+F, Ctrl+C, text selection, ...); Esc only counts while something is selected.
	/// </summary>
	public InputKeyAction KeyAction(InputNavKey key, bool hasModifiers, bool selected, double viewportHeight)
	{
		if (hasModifiers)
			return InputKeyAction.None;

		var page = Math.Max(Constants.InputNav.LineStepPx, Math.Floor(viewportHeight * Constants.InputNav.PageFraction));
		return key switch
		{
			InputNavKey.Up       => selected ? new InputKeyAction(InputKeyActionKind.Navigate, -1) : new InputKeyAction(InputKeyActionKind.Scroll, 0, -Constants.InputNav.LineStepPx),
			InputNavKey.Down     => selected ? new InputKeyAction(InputKeyActionKind.Navigate, 1) : new InputKeyAction(InputKeyActionKind.Scroll, 0, Constants.InputNav.LineStepPx),
			InputNavKey.PageUp   => new InputKeyAction(InputKeyActionKind.Scroll, 0, -page),
			InputNavKey.PageDown => new InputKeyAction(InputKeyActionKind.Scroll, 0, page),
			InputNavKey.Escape   => selected ? new InputKeyAction(InputKeyActionKind.Clear) : InputKeyAction.None,
			_                    => InputKeyAction.None,
		};
	}

	/// <summary>
	/// The scroll offset that brings an entry into view: unchanged when it is fully visible, otherwise the least scroll that shows it with
	/// <paramref name="margin"/> pixels to spare; an entry taller than the viewport is aligned to the top. Never negative.
	/// </summary>
	public double OffsetToReveal(double offset, double viewportHeight, double itemTop, double itemHeight, double margin)
	{
		var top = itemTop - margin;
		var bottom = itemTop + itemHeight + margin;
		if (top >= offset && bottom <= offset + viewportHeight)
			return offset;

		if (itemHeight + 2 * margin > viewportHeight || top < offset)
			return Math.Max(0, top);

		return Math.Max(0, bottom - viewportHeight);
	}

	/// <summary>True when a press and release this far apart (and without a text selection made meanwhile) is a click that selects an input.</summary>
	public bool IsSelectingClick(double dragDistance, bool madeTextSelection) =>
		!madeTextSelection && dragDistance <= Constants.InputNav.ClickDragTolerancePx;
}
