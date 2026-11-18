using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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
	/// <summary>TC.PW.69 (FR.27.10): the address box's focus and key handlers drive the view model (a test window never gets real keyboard focus, so the events are raised directly).</summary>
	[Fact]
	public void AddressBox_FocusAndKeys_DriveTheEditing()
	{
		StaTestRunner.Run(() =>
		{
			var surface = new FakeBrowserSurface();
			var vm = new BrowserPreviewViewModel(surface, new UrlPolicy(), new FakeUrlLauncher(), new ImmediateUiDispatcher(), NullLogger<BrowserPreviewViewModel>.Instance);
			var view = new BrowserPreviewView { DataContext = vm };
			view.Measure(new Size(900, 600));
			view.Arrange(new Rect(0, 0, 900, 600));
			view.UpdateLayout();
			surface.Attach();
			vm.Navigate("https://example.com/a");
			var box = (TextBox)view.FindName("AddressBox")!;

			RaiseFocus(box, Keyboard.GotKeyboardFocusEvent);
			Assert.True(vm.IsEditingAddress);
			box.Text = "github.com/x";
			Assert.Equal("github.com/x", vm.AddressText);   // typing updates the view model as it goes

			RaiseKey(box, Key.Enter);
			Assert.Equal(["https://example.com/a", "https://github.com/x"], surface.Navigated);
			Assert.Equal("https://github.com/x", vm.Address);

			RaiseFocus(box, Keyboard.GotKeyboardFocusEvent);
			box.Text = "ftp://x";
			RaiseKey(box, Key.Enter);
			Assert.True(vm.HasError);
			Assert.Equal(2, surface.Navigated.Count);   // nothing more opened

			RaiseKey(box, Key.Escape);
			Assert.False(vm.HasError);
			Assert.Equal("https://github.com/x", box.Text);

			box.Text = "half typed";
			RaiseFocus(box, Keyboard.LostKeyboardFocusEvent);
			Assert.False(vm.IsEditingAddress);
			Assert.Equal("https://github.com/x", box.Text);
		});
	}

	private static void RaiseFocus(TextBox box, RoutedEvent routedEvent) =>
		box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, box) { RoutedEvent = routedEvent });

	private static void RaiseKey(TextBox box, Key key)
	{
		var source = PresentationSource.FromVisual(box) ?? new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters());
		box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
	}
}
