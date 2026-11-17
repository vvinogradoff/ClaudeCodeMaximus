using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The scrollbars are wide enough to grab with the mouse (Architect feedback, round 3): one theme constant (<see cref="Constants.Appearance.ScrollBarWidth"/>, the <c>ScrollBarWidth</c>
/// resource of Styles/ScrollBars.xaml and the transcript page's <c>--sbw</c>) of 12-14 px, one shared WPF scrollbar style that every list and text area picks up, a visible thumb and a hover state.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScrollBarWidthTests
{
	private static string Css() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Web", "transcript.html"));

	private static string Rule(string selector)
	{
		var m = Regex.Match(Css(), @"(?m)(?:^|\})" + Regex.Escape(selector) + @"\{([^}]*)\}");
		Assert.True(m.Success, "no rule " + selector);
		return m.Groups[1].Value;
	}

	private static double Px(string declarations, string property)
	{
		var m = Regex.Match(declarations, @"(?<![\w-])" + Regex.Escape(property) + @":\s*(\d+(?:\.\d+)?)px");
		Assert.True(m.Success, $"no {property} in {declarations}");
		return double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
	}

	[Fact]
	public void TheWidthIsAThemeConstant_AndTheXamlResourceAndThePageCarryTheSameValue()
	{
		Assert.InRange(Constants.Appearance.ScrollBarWidth, 12, 14);

		StaTestRunner.Run(() =>
		{
			var dictionary = new ResourceDictionary { Source = new Uri("/TessynDesktop.WPF;component/Styles/ScrollBars.xaml", UriKind.Relative) };
			Assert.Equal(Constants.Appearance.ScrollBarWidth, (double)dictionary[Constants.Appearance.ScrollBarWidthKey]);
		});

		var root = Regex.Match(Css(), @":root\{[^}]*--sbw:(\d+(?:\.\d+)?)px").Groups[1].Value;
		Assert.Equal(Constants.Appearance.ScrollBarWidth, double.Parse(root, System.Globalization.CultureInfo.InvariantCulture));
	}

	[Fact]
	public void TheTranscriptThumb_IsATwelveToFourteenPixelBar_WithAHoverAndADragState()
	{
		// The track takes the theme width, the thumb fills it less the (at most 2 px) insets on both sides.
		Assert.Contains("width:var(--sbw)", Rule("#sb"));
		var thumb = Rule("#sbt");
		var inset = Px(thumb, "left") + Px(thumb, "right");
		var thumbWidth = Constants.Appearance.ScrollBarWidth - inset;
		Assert.InRange(thumbWidth, 12, 14);
		Assert.Contains("background:", thumb);

		// Hover and drag change the colour (darker), so the thumb answers the mouse.
		var hover = Rule("#sbt:hover,#sb:hover #sbt");
		var drag = Rule("#sb.drag #sbt");
		Assert.Contains("background:", hover);
		Assert.Contains("background:", drag);
		Assert.NotEqual(thumb, hover);
		Assert.NotEqual(hover, drag);
	}

	private static ScrollViewer TallScroller()
	{
		var content = new StackPanel();
		for (var i = 0; i < 80; i++)
			content.Children.Add(new TextBlock { Text = "row " + i, Height = 24 });
		var viewer = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
		viewer.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/TessynDesktop.WPF;component/Styles/ScrollBars.xaml", UriKind.Relative) });
		viewer.Resources["TextFillColorSecondaryBrush"] = Brushes.DimGray;   // the Fluent theme's brush, which the thumb draws with
		return viewer;
	}

	private static T Find<T>(DependencyObject root, Func<T, bool> where) where T : DependencyObject
	{
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match && where(match))
				return match;

			var nested = Find(child, where);
			if (nested != null)
				return nested;
		}

		return null!;
	}

	[Fact]
	public void TheSharedStyle_MakesAVerticalScrollbarThemeWide_WithAVisibleThumb()
	{
		StaTestRunner.Run(() =>
		{
			var viewer = TallScroller();
			viewer.Measure(new Size(300, 300));
			viewer.Arrange(new Rect(0, 0, 300, 300));
			viewer.UpdateLayout();

			var bar = Find<ScrollBar>(viewer, b => b.Orientation == Orientation.Vertical);
			Assert.NotNull(bar);
			Assert.Equal(Visibility.Visible, bar.Visibility);
			Assert.Equal(Constants.Appearance.ScrollBarWidth, bar.ActualWidth, 0.5);

			var thumb = Find<Thumb>(bar, _ => true);
			Assert.NotNull(thumb);
			Assert.InRange(thumb.ActualWidth, 12, Constants.Appearance.ScrollBarWidth);
			Assert.True(thumb.ActualHeight >= 28, $"The thumb is not a hairline: {thumb.ActualHeight}");
			var bar2 = Find<Border>(thumb, b => b.Name == "Bar");
			Assert.NotNull(bar2.Background);   // the visible fill
			Assert.True(bar2.Opacity > 0.2);
		});
	}

	[Fact]
	public void TheThumb_ShowsAHoverAndADragState()
	{
		StaTestRunner.Run(() =>
		{
			var dictionary = new ResourceDictionary { Source = new Uri("/TessynDesktop.WPF;component/Styles/ScrollBars.xaml", UriKind.Relative) };
			var style = (Style)dictionary["ScrollBarThumbStyle"];
			var template = (ControlTemplate)style.Setters.OfType<Setter>().Single(s => s.Property == Control.TemplateProperty).Value;
			var triggers = template.Triggers.OfType<Trigger>().ToList();

			var hover = triggers.Single(t => t.Property == UIElement.IsMouseOverProperty);
			var drag = triggers.Single(t => t.Property == Thumb.IsDraggingProperty);
			var restOpacity = 0.4;
			var hoverOpacity = (double)hover.Setters.OfType<Setter>().Single(s => s.Property == UIElement.OpacityProperty).Value;
			var dragOpacity = (double)drag.Setters.OfType<Setter>().Single(s => s.Property == UIElement.OpacityProperty).Value;
			Assert.True(restOpacity < hoverOpacity && hoverOpacity < dragOpacity, $"Rest {restOpacity}, hover {hoverOpacity}, drag {dragOpacity}");
		});
	}

	[Fact]
	public void AHorizontalScrollbar_IsThemeHighWithTheSameThumb()
	{
		StaTestRunner.Run(() =>
		{
			var viewer = TallScroller();
			((StackPanel)viewer.Content).Width = 900;
			viewer.Measure(new Size(300, 300));
			viewer.Arrange(new Rect(0, 0, 300, 300));
			viewer.UpdateLayout();

			var bar = Find<ScrollBar>(viewer, b => b.Orientation == Orientation.Horizontal);
			Assert.NotNull(bar);
			Assert.Equal(Constants.Appearance.ScrollBarWidth, bar.ActualHeight, 0.5);
			var thumb = Find<Thumb>(bar, _ => true);
			Assert.InRange(thumb.ActualHeight, 12, Constants.Appearance.ScrollBarWidth);
		});
	}
}
