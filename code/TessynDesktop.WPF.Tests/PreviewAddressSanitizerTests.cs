using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.29.11 (TC.PW.64): which web addresses may be saved for a reopen: same site only, and never with sign-in parameters.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewAddressSanitizerTests
{
	private readonly PreviewAddressSanitizer _sanitizer = new();

	[Theory]
	[InlineData("https://app.example.com/callback?code=abc&x=1")]
	[InlineData("https://app.example.com/cb?X=1&STATE=zzz")]
	[InlineData("https://app.example.com/cb?access_token=1")]
	[InlineData("https://app.example.com/cb#access_token=1&token_type=bearer")]
	[InlineData("https://app.example.com/cb#/path?id_token=abc")]
	[InlineData("https://app.example.com/cb#state=1")]
	[InlineData("https://app.example.com/cb?api%5Fkey=1")]
	[InlineData("https://app.example.com/cb?SAMLResponse=PHNhbWw")]
	[InlineData("https://app.example.com/cb?oauth_verifier=1")]
	public void SignInParameters_DropTheQueryAndTheFragment(string address)
	{
		var saved = _sanitizer.Sanitize(address);

		Assert.StartsWith("https://app.example.com/", saved);
		Assert.DoesNotContain('?', saved);
		Assert.DoesNotContain('#', saved);
	}

	[Theory]
	[InlineData("https://github.com/o/r/pull/1")]
	[InlineData("https://github.com/search?q=tessyn&type=code")]
	[InlineData("https://example.com/a?page=2#section")]
	[InlineData("https://example.com/a?codex=1&keyboard=2")]
	public void OrdinaryAddresses_AreKept(string address)
	{
		Assert.Equal(address, _sanitizer.Sanitize(address));
	}

	[Fact]
	public void AParameterNamedLikeASecret_DropsItsFragmentToo()
	{
		Assert.Equal("https://example.com/a", _sanitizer.Sanitize("https://example.com/a?token=1#section"));
		Assert.Equal("https://example.com/a", _sanitizer.Sanitize("https://example.com/a?page=2#code=9"));
	}

	[Theory]
	[InlineData("https://example.com/a", "https://example.com/b?x=1", true)]
	[InlineData("https://Example.com/a", "https://example.com/", true)]
	[InlineData("https://example.com/a", "http://example.com/a", false)]
	[InlineData("https://example.com/a", "https://login.example.com/a", false)]
	[InlineData("https://example.com/a", "https://example.com:8443/a", false)]
	[InlineData("https://example.com/a", "not an address", false)]
	[InlineData(null, "https://example.com/a", false)]
	public void SameOrigin_ComparesSchemeHostAndPort(string? first, string? second, bool expected)
	{
		Assert.Equal(expected, _sanitizer.IsSameOrigin(first, second));
	}
}
