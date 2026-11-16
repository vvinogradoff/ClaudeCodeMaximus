using System;
using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Line and "+N" counting of the tag area (FR.24.16), UI-free. Works on the laid-out chip positions, so the line height is the measured
/// chip height, not a constant. Mini mode shows exactly one line and counts the chips below it; full mode grows to fit up to
/// <see cref="Constants.TagArea.MaxFullLines"/> lines and then scrolls.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagAreaLayoutCalculator
{
	public TagAreaMetrics Compute(IReadOnlyList<TagChipBox> chips, bool expanded)
	{
		if (chips.Count == 0)
			return new TagAreaMetrics(0, null, 0, false);

		var lineHeight = 0.0;
		var contentBottom = 0.0;
		foreach (var chip in chips)
		{
			if (chip.Top < Constants.TagArea.LineEpsilon)
				lineHeight = Math.Max(lineHeight, chip.Height);
			contentBottom = Math.Max(contentBottom, chip.Top + chip.Height);
		}

		// Chips not measured yet (or squeezed to nothing by a zero-height clip): leave the height free so the next pass sees their real size.
		if (lineHeight <= 0)
			return new TagAreaMetrics(0, null, 0, false);

		if (expanded)
			return new TagAreaMetrics(lineHeight, Math.Min(contentBottom, lineHeight * Constants.TagArea.MaxFullLines), 0, true);

		var hidden = 0;
		foreach (var chip in chips)
		{
			if (chip.Top >= lineHeight - Constants.TagArea.LineEpsilon)
				hidden++;
		}

		return new TagAreaMetrics(lineHeight, lineHeight, hidden, false);
	}
}
