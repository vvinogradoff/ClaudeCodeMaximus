using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>TC.PW.69 (FR.27.10): what the user types into the address box becomes an http or https address, or is refused.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class BrowserAddressInputTests
{
	[Theory]
	[InlineData("github.com/x", "https://github.com/x")]
	[InlineData("  https://a.example/b?c=1  ", "https://a.example/b?c=1")]
	[InlineData("http://a.example/", "http://a.example/")]
	[InlineData("HTTPS://A.example/", "https://a.example/")]
	[InlineData("//a.example/p", "https://a.example/p")]
	[InlineData("localhost:3000", "https://localhost:3000/")]
	[InlineData("127.0.0.1:8080/x", "https://127.0.0.1:8080/x")]
	public void TryNormalize_AcceptsHttpAndHttps_AddingHttpsToABareHost(string typed, string expected)
	{
		Assert.True(BrowserAddressInput.TryNormalize(typed, new UrlPolicy(), out var uri));
		Assert.Equal(expected, uri.AbsoluteUri);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("ftp://x")]
	[InlineData("javascript:alert(1)")]
	[InlineData("mailto:a@b.c")]
	[InlineData("file:///C:/x.txt")]
	[InlineData("slack://open")]
	[InlineData("data:text/html,x")]
	public void TryNormalize_RefusesEverythingElse(string? typed)
	{
		Assert.False(BrowserAddressInput.TryNormalize(typed, new UrlPolicy(), out _));
	}
}
