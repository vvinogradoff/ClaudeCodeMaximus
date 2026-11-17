using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shell;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>Architect feedback: selectable Focus text, the application icon, a draggable build stamp, plain-coloured command-bar toggles.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class MiscFeedbackTests
{
	private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		var count = VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < count; i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				yield return match;
			foreach (var nested in Descendants<T>(child))
				yield return nested;
		}
	}

	private static void Layout(FrameworkElement element, double width = 800, double height = 100)
	{
		element.Measure(new Size(width, height));
		element.Arrange(new Rect(0, 0, width, height));
		element.UpdateLayout();
	}

	[Fact]
	public void SelectableText_IsReadOnlySelectableText_ThatKeepsItsContentAndLinks()
	{
		StaTestRunner.Run(() =>
		{
			var text = new SelectableText { Text = "A card title" };
			Layout(text, 300);

			Assert.True(text.IsReadOnly);
			Assert.True(text.IsDocumentEnabled);   // hyperlinks stay clickable
			Assert.True(text.IsHitTestVisible && text.Focusable);
			Assert.Equal("A card title", text.PlainText);

			text.SelectAll();
			Assert.Equal("A card title", text.Selection.Text.TrimEnd('\r', '\n'));
		});
	}

	[Fact]
	public void SelectableText_IsTextForUiAutomation_NamedByItsContent()
	{
		StaTestRunner.Run(() =>
		{
			var text = new SelectableText { Text = "Answered: Yes" };
			var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(text);

			Assert.Equal(System.Windows.Automation.Peers.AutomationControlType.Text, peer.GetAutomationControlType());
			Assert.Equal("Answered: Yes", peer.GetName());
		});
	}

	[Fact]
	public void TheBuildStamp_IsPartOfTheCaption_ButTheWindowButtonsStayClickable()
	{
		StaTestRunner.Run(() =>
		{
			var bar = new TitleBar();
			Layout(bar, 1200, 40);

			var stamp = (FrameworkElement)bar.FindName("BuildNumberText")!;
			var stampPanel = (FrameworkElement)stamp.Parent;
			Assert.False(WindowChrome.GetIsHitTestVisibleInChrome(stampPanel));
			Assert.False(WindowChrome.GetIsHitTestVisibleInChrome(stamp));
			Assert.False(stamp.Focusable);
			foreach (var name in new[] { "MinimizeButton", "MaximizeButton", "CloseButton" })
				Assert.True(WindowChrome.GetIsHitTestVisibleInChrome((FrameworkElement)bar.FindName(name)!), name);
		});
	}

	[Fact]
	public void TheCommandBarToggles_UseTheRegularButtonColour_AlsoWhenChecked_AndStandOffTheProfileSelector()
	{
		StaTestRunner.Run(() =>
		{
			var toggles = new CommandToggles();
			Layout(toggles);

			Assert.True(((FrameworkElement)toggles.Content).Margin.Left > 0, "a horizontal gap after the Profile selector");
			var buttons = Descendants<ToggleButton>(toggles).ToList();
			Assert.Equal(2, buttons.Count);
			foreach (var button in buttons)
			{
				button.IsEnabled = true;
				button.IsChecked = true;
				button.ApplyTemplate();
				button.UpdateLayout();
				var chrome = (Border)button.Template.FindName("Chrome", button);
				Assert.Same(button.Background, chrome.Background);   // the fill of the button itself, not the accent fill of a checked Fluent toggle
				Assert.NotNull(button.Style);   // PlainToggle: no accent trigger on IsChecked
			}
		});
	}

	[Fact]
	public void TheApplicationIcon_HasTheMultipleSizes_AndIsOnTheMainWindow()
	{
		var ico = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TessynDesktop.WPF", "Assets", "Tessyn.ico");
		var bytes = File.ReadAllBytes(ico);
		Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
		Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));   // an icon file
		Assert.True(BitConverter.ToUInt16(bytes, 4) >= 6, "16 to 256 px");

		var xaml = File.ReadAllText(Path.Combine(Path.GetDirectoryName(ico)!, "..", "Views", "MainWindow.xaml"));
		Assert.Contains("Assets/Tessyn.ico", xaml);
		var csproj = File.ReadAllText(Path.Combine(Path.GetDirectoryName(ico)!, "..", "TessynDesktop.WPF.csproj"));
		Assert.Contains("<ApplicationIcon>Assets\\Tessyn.ico</ApplicationIcon>", csproj);
	}
}
