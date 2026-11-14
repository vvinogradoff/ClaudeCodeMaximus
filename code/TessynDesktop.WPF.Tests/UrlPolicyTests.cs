using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>Only absolute http and https URLs may leave the app.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class UrlPolicyTests
{
	private readonly UrlPolicy _policy = new();

	[Theory]
	[InlineData("https://example.com")]
	[InlineData("http://example.com/path?q=1")]
	[InlineData("  https://example.com/padded  ")]
	[InlineData("HTTPS://EXAMPLE.COM")]
	public void Allows_HttpAndHttps(string url) => Assert.True(_policy.IsAllowed(url));

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("javascript:alert(1)")]
	[InlineData("file:///c:/x")]
	[InlineData("data:text/html,<b>x</b>")]
	[InlineData("ftp://example.com")]
	[InlineData("mailto:a@b.c")]
	[InlineData("/relative/path")]
	[InlineData("//example.com")]
	[InlineData("ms-settings:privacy")]
	[InlineData("C:\\Windows\\System32\\calc.exe")]
	public void Refuses_EverythingElse(string? url) => Assert.False(_policy.IsAllowed(url));
}
