using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.27.2: the embedded browser navigates over http, https and about:blank only.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class BrowserNavigationPolicyTests
{
	private readonly BrowserNavigationPolicy _policy = new();

	[Theory]
	[InlineData("http://example.com/")]
	[InlineData("https://hardstop.slack.com/ssb/redirect")]
	[InlineData("HTTPS://GITHUB.COM/login?x=1#y")]
	[InlineData("about:blank")]
	[InlineData("ABOUT:BLANK")]
	public void Allowed(string uri) => Assert.True(_policy.IsAllowedTopLevel(uri));

	[Theory]
	[InlineData("slack://channel?team=T1")]
	[InlineData("mailto:a@b.c")]
	[InlineData("tel:+123")]
	[InlineData("file:///C:/Windows/win.ini")]
	[InlineData("javascript:alert(1)")]
	[InlineData("data:text/html,<b>x</b>")]
	[InlineData("blob:https://example.com/1")]
	[InlineData("ms-settings:network")]
	[InlineData("about:srcdoc")]
	[InlineData("/relative/path")]
	[InlineData("example.com")]
	[InlineData("not a url")]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData(null)]
	public void Refused(string? uri) => Assert.False(_policy.IsAllowedTopLevel(uri));
}
