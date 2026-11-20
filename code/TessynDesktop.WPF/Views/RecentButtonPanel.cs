using System.Windows;
using System.Windows.Controls;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The content layout of the closed Recent button (FR.1.33 item 5). Five children in a fixed order: the session name, the profile name, the "→ target",
/// the workers badge and the chevron. The badge and the chevron are measured first and always keep their places and sizes at the right edge; the width
/// left is given to the profile first, then the "→ target", and the name gets what remains, so the name trims first, then the target, and the profile
/// only after both are gone. A hidden child takes no room. Each text child trims itself (<c>TextTrimming</c>) when it gets less than it wants.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class RecentButtonPanel : Panel
{
	private const int NameIndex = 0;
	private const int ProfileIndex = 1;
	private const int TargetIndex = 2;
	private const int BadgeIndex = 3;
	private const int ChevronIndex = 4;
	private const int ChildCount = 5;

	private readonly double[] _natural = new double[ChildCount];

	protected override Size MeasureOverride(Size availableSize)
	{
		if (InternalChildren.Count != ChildCount)
			return base.MeasureOverride(availableSize);

		var height = 0.0;
		for (var i = 0; i < ChildCount; i++)
		{
			var child = InternalChildren[i];
			child.Measure(new Size(double.PositiveInfinity, availableSize.Height));   // what it wants
			_natural[i] = child.DesiredSize.Width;
			height = Math.Max(height, child.DesiredSize.Height);
		}

		var widths = Allocate(availableSize.Width);
		for (var i = 0; i < ChildCount; i++)
			InternalChildren[i].Measure(new Size(widths[i], availableSize.Height));   // a text trims itself to what it was given

		return new Size(widths.Sum(), height);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		if (InternalChildren.Count != ChildCount)
			return base.ArrangeOverride(finalSize);

		var widths = Allocate(finalSize.Width);
		var right = finalSize.Width;
		foreach (var index in new[] { ChevronIndex, BadgeIndex, TargetIndex, ProfileIndex })
		{
			right -= widths[index];
			InternalChildren[index].Arrange(new Rect(right, 0, widths[index], finalSize.Height));
		}

		InternalChildren[NameIndex].Arrange(new Rect(0, 0, Math.Max(0, right), finalSize.Height));   // the name fills what is left, flush left
		return finalSize;
	}

	/// <summary>The width of each child: the badge and the chevron in full, then the profile, the target and the name in that order, each min(what it wants, what is left).</summary>
	private double[] Allocate(double available)
	{
		var widths = new double[ChildCount];
		var left = double.IsInfinity(available) ? double.PositiveInfinity : Math.Max(0, available);
		foreach (var index in new[] { BadgeIndex, ChevronIndex, ProfileIndex, TargetIndex, NameIndex })
		{
			widths[index] = Math.Min(_natural[index], left);
			left -= widths[index];
		}

		return widths;
	}
}
