using System.Windows;
using System.Windows.Controls;

namespace TessynDesktop.WPF.Views.Settings;

/// <summary>
/// Lays its children out in two equal columns, row by row (left, then right), and falls back to one column when the width offered is under <see cref="MinTwoColumnWidth"/> (FR.9.18).
/// A row is as tall as its taller cell. Not virtualising: the colour list is small.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TwoColumnPanel : Panel
{
	public static readonly DependencyProperty MinTwoColumnWidthProperty = DependencyProperty.Register(
		nameof(MinTwoColumnWidth), typeof(double), typeof(TwoColumnPanel), new FrameworkPropertyMetadata(600.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

	/// <summary>The width from which two columns are used; narrower, one.</summary>
	public double MinTwoColumnWidth
	{
		get => (double)GetValue(MinTwoColumnWidthProperty);
		set => SetValue(MinTwoColumnWidthProperty, value);
	}

	private int ColumnsFor(double width) => width >= MinTwoColumnWidth ? 2 : 1;

	protected override Size MeasureOverride(Size availableSize)
	{
		var columns = ColumnsFor(availableSize.Width);
		var cell = new Size(double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width / columns, double.PositiveInfinity);
		var height = 0.0;
		for (var i = 0; i < InternalChildren.Count; i += columns)
		{
			var rowHeight = 0.0;
			for (var c = 0; c < columns && i + c < InternalChildren.Count; c++)
			{
				InternalChildren[i + c].Measure(cell);
				rowHeight = Math.Max(rowHeight, InternalChildren[i + c].DesiredSize.Height);
			}

			height += rowHeight;
		}

		return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, height);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var columns = ColumnsFor(finalSize.Width);
		var cellWidth = finalSize.Width / columns;
		var top = 0.0;
		for (var i = 0; i < InternalChildren.Count; i += columns)
		{
			var rowHeight = 0.0;
			for (var c = 0; c < columns && i + c < InternalChildren.Count; c++)
				rowHeight = Math.Max(rowHeight, InternalChildren[i + c].DesiredSize.Height);

			for (var c = 0; c < columns && i + c < InternalChildren.Count; c++)
				InternalChildren[i + c].Arrange(new Rect(c * cellWidth, top, cellWidth, rowHeight));

			top += rowHeight;
		}

		return finalSize;
	}
}
