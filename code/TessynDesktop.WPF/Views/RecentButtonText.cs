using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// A text of the closed Recent button. <see cref="RecentButtonPanel"/> measures it at the width it allots, so a text trimmed to nothing keeps a desired
/// size of zero when it changes and WPF would not tell the panel; this text invalidates the panel's measure itself when the text or its font size changes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class RecentButtonText : TextBlock
{
	static RecentButtonText()
	{
		TextProperty.OverrideMetadata(typeof(RecentButtonText), new FrameworkPropertyMetadata(string.Empty, InvalidatePanel));
		FontSizeProperty.OverrideMetadata(typeof(RecentButtonText), new FrameworkPropertyMetadata(SystemFonts.MessageFontSize, FrameworkPropertyMetadataOptions.Inherits, InvalidatePanel));
	}

	private static void InvalidatePanel(DependencyObject sender, DependencyPropertyChangedEventArgs e) => (VisualTreeHelper.GetParent(sender) as UIElement)?.InvalidateMeasure();
}
