using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;
// <remarks>Created by Claude</remarks>
public sealed class RecentSessionVisibilityServiceTests
{
    private static bool Show(
        bool isFavorite = false, bool hasOpenAttention = false, bool hasSchedules = false,
        bool isRecentlyActive = false, bool isHiddenFromRecent = false) =>
        RecentSessionVisibilityService.ShouldShowInRecentDropdown(
            isFavorite: isFavorite, hasOpenAttention: hasOpenAttention, hasSchedules: hasSchedules,
            isRecentlyActive: isRecentlyActive, isHiddenFromRecent: isHiddenFromRecent);

    [Fact]
    public void ShouldShowInRecentDropdown_FavoriteStaleAndHidden_ReturnsTrue()
    {
        Assert.True(Show(isFavorite: true, isRecentlyActive: false, isHiddenFromRecent: true));
    }

    [Fact]
    public void ShouldShowInRecentDropdown_OpenAttentionStaleAndHidden_ReturnsTrue()
    {
        Assert.True(Show(hasOpenAttention: true, isRecentlyActive: false, isHiddenFromRecent: true));
    }

    [Fact]
    public void ShouldShowInRecentDropdown_SchedulesStaleAndHidden_ReturnsTrue()
    {
        Assert.True(Show(hasSchedules: true, isRecentlyActive: false, isHiddenFromRecent: true));
    }

    [Fact]
    public void ShouldShowInRecentDropdown_NonFavoriteRecentAndNotHidden_ReturnsTrue()
    {
        Assert.True(Show(isRecentlyActive: true, isHiddenFromRecent: false));
    }

    [Fact]
    public void ShouldShowInRecentDropdown_NotPinnedStale_ReturnsFalse()
    {
        Assert.False(Show(isRecentlyActive: false, isHiddenFromRecent: false));
    }

    [Fact]
    public void ShouldShowInRecentDropdown_NotPinnedStaleAndHidden_ReturnsFalse()
    {
        Assert.False(Show(isRecentlyActive: false, isHiddenFromRecent: true));
    }

    [Fact]
    public void ShouldShowInRecentDropdown_NotPinnedRecentButHidden_ReturnsFalse()
    {
        Assert.False(Show(isRecentlyActive: true, isHiddenFromRecent: true));
    }

    [Fact]
    public void ShouldShowInRecentDropdown_FavoriteRecentAndHidden_ReturnsTrue()
    {
        Assert.True(Show(isFavorite: true, isRecentlyActive: true, isHiddenFromRecent: true));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void IsPinnedInRecentDropdown_AnyReason_ReturnsTrue(bool isFavorite, bool hasOpenAttention, bool hasSchedules)
    {
        Assert.True(RecentSessionVisibilityService.IsPinnedInRecentDropdown(
            isFavorite: isFavorite, hasOpenAttention: hasOpenAttention, hasSchedules: hasSchedules));
    }

    [Fact]
    public void IsPinnedInRecentDropdown_NoReason_ReturnsFalse()
    {
        Assert.False(RecentSessionVisibilityService.IsPinnedInRecentDropdown(
            isFavorite: false, hasOpenAttention: false, hasSchedules: false));
    }
}
