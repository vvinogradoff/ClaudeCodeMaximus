using System;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>The pure FR.9.6 rules (24-hour window, hidden until newer activity) that <c>recent.list</c> shares with the desktop dropdown.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class RecentSessionVisibilityRulesTests
{
	private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

	[Theory]
	[InlineData(0, true)]
	[InlineData(23, true)]
	[InlineData(24, true)]      // inclusive, as on the desktop (<= 24 h)
	[InlineData(25, false)]
	public void IsRecentlyActive_WithinTwentyFourHours(double hoursAgo, bool expected) =>
		Assert.Equal(expected, RecentSessionVisibilityService.IsRecentlyActive(Now.AddHours(-hoursAgo), Now));

	[Fact]
	public void IsRecentlyActive_NoActivity_IsFalse() =>
		Assert.False(RecentSessionVisibilityService.IsRecentlyActive(null, Now));

	[Fact]
	public void IsHiddenFromRecent_NotIgnored_IsFalse()
	{
		Assert.False(RecentSessionVisibilityService.IsHiddenFromRecent(null, Now));
		Assert.False(RecentSessionVisibilityService.IsHiddenFromRecent(null, null));
	}

	[Fact]
	public void IsHiddenFromRecent_IgnoredAfterTheLastActivity_IsTrue_AndNewerActivityRevivesIt()
	{
		var ignored = Now;

		Assert.True(RecentSessionVisibilityService.IsHiddenFromRecent(ignored, ignored.AddMinutes(-1)));
		Assert.True(RecentSessionVisibilityService.IsHiddenFromRecent(ignored, ignored));
		Assert.False(RecentSessionVisibilityService.IsHiddenFromRecent(ignored, ignored.AddMinutes(1)));
	}

	[Fact]
	public void IsHiddenFromRecent_IgnoredWithoutAnyActivity_IsTrue() =>
		Assert.True(RecentSessionVisibilityService.IsHiddenFromRecent(Now, null));
}
