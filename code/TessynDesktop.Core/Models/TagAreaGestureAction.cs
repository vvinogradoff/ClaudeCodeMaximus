namespace TessynDesktop.Models;

/// <summary>What a mouse press on the tag area does (FR.24.16).</summary>
/// <remarks>Created by Claude</remarks>
public enum TagAreaGestureAction
{
	/// <summary>Leave the press alone (a chip click opens its link, a scrollbar press scrolls).</summary>
	None,

	/// <summary>Switch to full mode.</summary>
	Expand,

	/// <summary>Switch to mini mode and consume the press.</summary>
	Collapse,

	/// <summary>Consume the press without changing the mode (it belongs to the click that just expanded the area).</summary>
	Swallow,
}
