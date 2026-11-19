using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>The preview page lets only the html frame's own srcdoc load through (FR.26.8).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FramePolicyTests
{
	private readonly FramePolicy _policy = new();

	[Fact]
	public void OnlyTheSrcdocLoad_IsAllowed() => Assert.True(_policy.IsAllowed("about:srcdoc"));

	[Theory]
	[InlineData("https://example.com/")]
	[InlineData("about:blank")]
	[InlineData("javascript:alert(1)")]
	[InlineData("data:text/html,<p>x</p>")]
	[InlineData("https://app.local/preview.html")]
	[InlineData("")]
	[InlineData(null)]
	public void EveryOtherFrameNavigation_IsCancelled(string? uri) => Assert.False(_policy.IsAllowed(uri));
}
