using System;
using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Line and "+N" counting of the tag area (FR.24.16), UI-free. Works on the laid-out chip positions, so the line height is measured, not a
/// constant. Mini mode shows exactly one line and counts the chips below it; full mode grows to fit up to
/// <see cref="Constants.TagArea.MaxFullLines"/> lines and then scrolls.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagAreaLayoutCalculator
{
	/// <param name="chips">The laid-out chips.</param>
	/// <param name="expanded">Full (true) or mini (false) mode.</param>
	/// <param name="unreservedWidth">
	/// Mini mode: the width of the line without the "+N" hint's reserve. When every chip would fit on one line in it, no chip is hidden and the hint
	/// is not needed, even though the reserve currently pushes the last chip down. NaN or 0 = unknown.
	/// </param>
	public TagAreaMetrics Compute(IReadOnlyList<TagChipBox> chips, bool expanded, double unreservedWidth = double.NaN)
	{
		if (chips.Count == 0)
			return new TagAreaMetrics(0, null, 0, false);

		var lineHeight = 0.0;
		var contentBottom = 0.0;
		var secondLineTop = double.PositiveInfinity;
		var totalWidth = 0.0;
		foreach (var chip in chips)
		{
			if (chip.Top < Constants.TagArea.LineEpsilon)
				lineHeight = Math.Max(lineHeight, chip.Height);
			else
				secondLineTop = Math.Min(secondLineTop, chip.Top);

			contentBottom = Math.Max(contentBottom, chip.Top + chip.Height);
			totalWidth += chip.Width;
		}

		// Chips not measured yet (or squeezed to nothing by a zero-height clip): leave the height free so the next pass sees their real size.
		if (lineHeight <= 0)
			return new TagAreaMetrics(0, null, 0, false);

		// The pitch of a line is where the next line starts: a chip's margin sits between the lines, so it is more than the chip height.
		// Without a second line the chip height is all there is to know.
		var pitch = double.IsPositiveInfinity(secondLineTop) ? lineHeight : Math.Max(lineHeight, secondLineTop);

		if (expanded)
			return new TagAreaMetrics(pitch, Math.Min(contentBottom, pitch * Constants.TagArea.MaxFullLines), 0, true);

		var hidden = 0;
		foreach (var chip in chips)
		{
			if (chip.Top >= pitch - Constants.TagArea.LineEpsilon)
				hidden++;
		}

		if (hidden > 0 && unreservedWidth > 0 && totalWidth <= unreservedWidth + Constants.TagArea.LineEpsilon)
			hidden = 0;

		return new TagAreaMetrics(pitch, pitch, hidden, false);
	}
}
