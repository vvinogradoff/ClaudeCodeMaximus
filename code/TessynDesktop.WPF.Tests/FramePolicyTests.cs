using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>The preview page lets only the one srcdoc load of each html preview through (FR.26.8).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FramePolicyTests
{
	private readonly FramePolicy _policy = new();

	[Fact]
	public void TheExpectedSrcdocLoad_IsAllowedOnce()
	{
		_policy.ExpectSrcdocLoad();

		Assert.True(_policy.IsAllowed("about:srcdoc"));
		Assert.False(_policy.IsAllowed("about:srcdoc"));   // a refresh meta re-navigating to the same document
	}

	[Fact]
	public void WithoutAnExpectation_NothingIsAllowed() => Assert.False(_policy.IsAllowed("about:srcdoc"));

	[Fact]
	public void AnotherHtmlPreview_AllowsOneMoreLoad()
	{
		_policy.ExpectSrcdocLoad();
		Assert.True(_policy.IsAllowed("about:srcdoc"));

		_policy.ExpectSrcdocLoad();
		Assert.True(_policy.IsAllowed("about:srcdoc"));
		Assert.False(_policy.IsAllowed("about:srcdoc"));
	}

	[Fact]
	public void TwoStartsBeforeTheLoad_StillAllowOnlyOne()
	{
		_policy.ExpectSrcdocLoad();
		_policy.ExpectSrcdocLoad();

		Assert.True(_policy.IsAllowed("about:srcdoc"));
		Assert.False(_policy.IsAllowed("about:srcdoc"));
	}

	[Fact]
	public void ANonHtmlPreview_DropsTheExpectation()
	{
		_policy.ExpectSrcdocLoad();
		_policy.ExpectNothing();

		Assert.False(_policy.IsAllowed("about:srcdoc"));
	}

	[Theory]
	[InlineData("https://example.com/")]
	[InlineData("about:blank")]
	[InlineData("javascript:alert(1)")]
	[InlineData("data:text/html,<p>x</p>")]
	[InlineData("https://app.local/preview.html")]
	[InlineData("")]
	[InlineData(null)]
	public void EveryOtherFrameNavigation_IsCancelled_AndKeepsTheExpectation(string? uri)
	{
		_policy.ExpectSrcdocLoad();

		Assert.False(_policy.IsAllowed(uri));
		Assert.True(_policy.IsAllowed("about:srcdoc"));
	}
}
