using Microsoft.Web.WebView2.Wpf;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.6.13: the Esc tap over a WebView2 page logs its warning once per tap instance (the factory shares one for every surface), not once per surface.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class WebViewEscapeTapTests
{
	[Fact]
	public void AControlWithoutAController_WarnsOnce_ForAllTheSurfacesOfOneTap_AndHooksNothing()
	{
		StaTestRunner.Run(() =>
		{
			var log = new CapturingLogger<WebViewEscapeTapTests>();
			var tap = new WebViewEscapeTap();

			var first = tap.Hook(new WebView2(), () => { }, log);   // the browser is not created yet: no controller
			var second = tap.Hook(new WebView2(), () => { }, log);

			Assert.False(first);
			Assert.False(second);
			Assert.Single(log.Messages);
			Assert.Contains(Constants.WebViewEscapeTap.BaseField, log.Messages[0]);

			new WebViewEscapeTap().Hook(new WebView2(), () => { }, log);   // another process-wide instance would warn again; the app has one
			Assert.Equal(2, log.Messages.Count);
		});
	}
}
