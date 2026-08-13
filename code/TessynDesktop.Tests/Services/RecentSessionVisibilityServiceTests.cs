using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;
// <remarks>Created by Claude</remarks>
public sealed class RecentSessionVisibilityServiceTests
{
    [Fact]
    public void ShouldShowInRecentDropdown_FavoriteStaleAndHidden_ReturnsTrue()
    {
        Assert.True(RecentSessionVisibilityService.ShouldShowInRecentDropdown(
            isFavorite: true, isRecentlyActive: false, isHiddenFromRecent: true));
    }

    [Fact]
    public void ShouldShowInRecentDropdown_NonFavoriteRecentAndNotHidden_ReturnsTrue()
    {
        Assert.True(RecentSessionVisibilityService.ShouldShowInRecentDropdown(
            isFavorite: false, isRecentlyActive: true, isHiddenFromRecent: false));
    }

    [Fact]
    public void ShouldShowInRecentDropdown_NonFavoriteStale_ReturnsFalse()
    {
        Assert.False(RecentSessionVisibilityService.ShouldShowInRecentDropdown(
            isFavorite: false, isRecentlyActive: false, isHiddenFromRecent: false));
    }

    [Fact]
    public void ShouldShowInRecentDropdown_NonFavoriteHidden_ReturnsFalse()
    {
        Assert.False(RecentSessionVisibilityService.ShouldShowInRecentDropdown(
            isFavorite: false, isRecentlyActive: true, isHiddenFromRecent: true));
    }

    [Fact]
    public void ShouldShowInRecentDropdown_FavoriteRecentAndHidden_ReturnsTrue()
    {
        Assert.True(RecentSessionVisibilityService.ShouldShowInRecentDropdown(
            isFavorite: true, isRecentlyActive: true, isHiddenFromRecent: true));
    }
}
