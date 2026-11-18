using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.27.4: the browser bar view builds, names its controls, and creates no WebView2 before it is loaded.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class BrowserPreviewViewTests
{
	private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < count; i++)
		{
			var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				yield return match;
			foreach (var nested in Descendants<T>(child))
				yield return nested;
		}
	}

	[Fact]
	public void View_BuildsWithTheBarControls_AndNoWebView2BeforeLoad()
	{
		StaTestRunner.Run(() =>
		{
			var vm = new BrowserPreviewViewModel(new FakeBrowserSurface(), new UrlPolicy(), new FakeUrlLauncher(), new ImmediateUiDispatcher(), NullLogger<BrowserPreviewViewModel>.Instance);
			var view = new BrowserPreviewView { DataContext = vm };
			view.Measure(new Size(900, 600));
			view.Arrange(new Rect(0, 0, 900, 600));
			view.UpdateLayout();

			var names = Descendants<Control>(view).Select(c => AutomationProperties.GetName(c)).Where(n => !string.IsNullOrEmpty(n)).ToList();
			foreach (var expected in new[] { "Browser back", "Browser forward", "Reload page", "Page address", "Open in system browser" })
				Assert.Contains(expected, names);

			Assert.Null(view.Page);
			Assert.Empty(Descendants<Microsoft.Web.WebView2.Wpf.WebView2>(view));

			var address = Descendants<TextBox>(view).Single();
			Assert.False(address.IsReadOnly);   // FR.27.10
			vm.Navigate("https://example.com/x");
			view.UpdateLayout();
			Assert.Equal("https://example.com/x", address.Text);
		});
	}
}
