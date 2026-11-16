using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Decides what a mouse press on the tag area does (FR.24.16), UI-free: a click on empty space or on the "+N" hint expands mini mode; a double-click
/// anywhere (chip or empty space) collapses full mode; a chip click opens its link (the view does that); the scrollbar keeps its own meaning.
/// Remembers that the current click sequence expanded the area, so its second, third, ... press does not fold it again.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagAreaGestureInterpreter
{
	private bool _expandedByThisClick;

	public TagAreaGestureAction OnPress(int clickCount, bool onChip, bool onScrollBar, bool expanded, bool hasTags)
	{
		if (!hasTags || onScrollBar)
			return TagAreaGestureAction.None;

		if (clickCount >= 2)
		{
			if (_expandedByThisClick)
				return TagAreaGestureAction.Swallow;

			return expanded ? TagAreaGestureAction.Collapse : TagAreaGestureAction.None;
		}

		_expandedByThisClick = false;
		if (!expanded && !onChip)
		{
			_expandedByThisClick = true;
			return TagAreaGestureAction.Expand;
		}

		return TagAreaGestureAction.None;
	}
}
