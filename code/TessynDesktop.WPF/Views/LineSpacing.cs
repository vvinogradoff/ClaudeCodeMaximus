using System.Windows;
using System.Windows.Controls;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// <c>LineSpacing.Factor</c> (FR.9.12): an inherited attached property. Set once on a panel (from the <c>LineSpacing</c> application resource), it gives every
/// <see cref="TextBlock"/> below a fixed line height of font size x factor. 0 or unset leaves the text at its natural line height.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class LineSpacing
{
	public static readonly DependencyProperty FactorProperty = DependencyProperty.RegisterAttached(
		"Factor", typeof(double), typeof(LineSpacing),
		new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.Inherits, OnFactorChanged));

	public static double GetFactor(DependencyObject element) => (double)element.GetValue(FactorProperty);

	public static void SetFactor(DependencyObject element, double value) => element.SetValue(FactorProperty, value);

	private static void OnFactorChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
	{
		if (sender is TextBlock text)
			Apply(text, (double)e.NewValue);
	}

	private static void Apply(TextBlock text, double factor)
	{
		if (factor >= 1)
		{
			text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			text.LineHeight = Math.Max(1, text.FontSize * factor);
		}
		else
		{
			text.ClearValue(TextBlock.LineHeightProperty);
			text.ClearValue(TextBlock.LineStackingStrategyProperty);
		}
	}
}
