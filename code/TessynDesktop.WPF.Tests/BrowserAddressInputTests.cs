using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>TC.PW.69 (FR.27.10): what the user types into the address box becomes an http or https address, or is refused.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class BrowserAddressInputTests
{
	private readonly BrowserAddressInput _input = new(new UrlPolicy());

	[Theory]
	[InlineData("github.com/x", "https://github.com/x")]
	[InlineData("  https://a.example/b?c=1  ", "https://a.example/b?c=1")]
	[InlineData("http://a.example/", "http://a.example/")]
	[InlineData("HTTPS://A.example/", "https://a.example/")]
	[InlineData("//a.example/p", "https://a.example/p")]
	[InlineData("localhost:3000", "https://localhost:3000/")]
	[InlineData("127.0.0.1:8080/x", "https://127.0.0.1:8080/x")]
	[InlineData("localhost:8080", "https://localhost:8080/")]
	[InlineData("https://bücher.example/x", "https://bücher.example/x")]
	[InlineData("bücher.example", "https://bücher.example/")]
	public void TryNormalize_AcceptsHttpAndHttps_AddingHttpsToABareHost(string typed, string expected)
	{
		Assert.True(_input.TryNormalize(typed, out var uri));
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
	[InlineData("javascript:1")]
	[InlineData("mailto:5")]
	[InlineData("about:blank")]
	[InlineData("JavaScript:alert(1)")]
	[InlineData("tel:5551234")]
	[InlineData("view-source:https://a.example/")]
	[InlineData("two words.example")]
	[InlineData("a.example/with space")]
	public void TryNormalize_RefusesEverythingElse(string? typed)
	{
		Assert.False(_input.TryNormalize(typed, out _));
	}
}
