using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>TC.TG.17 — <see cref="SessionTagUrlPolicy"/> (FR.24.7).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagUrlPolicyTests
{
	[Theory]
	[InlineData("https://linear.app/hardstop/issue/HSE-230")]
	[InlineData("https://github.com/tryhardstop/windows-app/pull/215")]
	[InlineData("https://github.com/o/r")]
	[InlineData("https://LINEAR.APP/x")]
	public void IsOpenable_AllowedUrls(string url)   // TC.TG.17-1
	{
		Assert.True(SessionTagUrlPolicy.IsOpenable(url));
	}

	[Theory]
	[InlineData("http://linear.app/x")]
	[InlineData("http://github.com/o/r")]
	[InlineData("https://github.com.evil.io/o/r")]
	[InlineData("https://evil.io/github.com")]
	[InlineData("https://linear.app.evil.io/x")]
	[InlineData("https://www.github.com/o/r")]
	[InlineData("https://evil.io@github.com.evil.io/")]
	[InlineData("https://github.com@evil.io/")]
	[InlineData("file:///C:/Windows/System32/calc.exe")]
	[InlineData("ftp://github.com/o/r")]
	[InlineData("javascript:alert(1)")]
	[InlineData("/relative/path")]
	[InlineData("github.com/o/r")]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData(null)]
	public void IsOpenable_RejectedUrls(string? url)   // TC.TG.17-2
	{
		Assert.False(SessionTagUrlPolicy.IsOpenable(url));
	}
}
