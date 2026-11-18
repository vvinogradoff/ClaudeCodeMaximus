using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>Review 2 of FR.29: a popup takes the focus only when the user asked for it, and one surface keeps at most five.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class BrowserPopupPolicyTests
{
	private readonly BrowserPopupPolicy _policy = new();

	[Fact]
	public void AUserInitiatedRequest_OpensAnActivePopup()
	{
		Assert.Equal(BrowserPopupDecision.ShowActive, _policy.Decide(0, userInitiated: true));
	}

	[Fact]
	public void AScriptOrTimerRequest_OpensAnInactivePopup()
	{
		Assert.Equal(BrowserPopupDecision.ShowInactive, _policy.Decide(0, userInitiated: false));
	}

	[Fact]
	public void TheCap_DropsEveryRequest_UserInitiatedOrNot()
	{
		var last = Constants.Browser.MaxPopups - 1;
		Assert.Equal(BrowserPopupDecision.ShowActive, _policy.Decide(last, userInitiated: true));

		Assert.Equal(BrowserPopupDecision.Drop, _policy.Decide(Constants.Browser.MaxPopups, userInitiated: true));
		Assert.Equal(BrowserPopupDecision.Drop, _policy.Decide(Constants.Browser.MaxPopups + 3, userInitiated: false));
	}

	[Fact]
	public void TheCapIsInjectable()
	{
		var small = new BrowserPopupPolicy(maxPopups: 1);

		Assert.Equal(BrowserPopupDecision.ShowInactive, small.Decide(0, false));
		Assert.Equal(BrowserPopupDecision.Drop, small.Decide(1, false));
	}
}
