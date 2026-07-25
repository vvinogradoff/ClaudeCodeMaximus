using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;
// <remarks>Created by Claude</remarks>
public sealed class ModelTierServiceTests
{
    [Theory]
    [InlineData("fable", 4)]
    [InlineData("opus", 3)]
    [InlineData("sonnet", 2)]
    [InlineData("haiku", 1)]
    public void GetTier_Keyword_ReturnsExpectedTier(string keyword, int expected)
    {
        Assert.Equal(expected, ModelTierService.GetTier(keyword));
    }

    [Theory]
    [InlineData("claude-fable-5", 4)]
    [InlineData("claude-opus-4-8", 3)]
    [InlineData("claude-sonnet-4-6", 2)]
    [InlineData("claude-haiku-4-5-20251001", 1)]
    public void GetTier_RealModelId_ReturnsExpectedTier(string modelId, int expected)
    {
        Assert.Equal(expected, ModelTierService.GetTier(modelId));
    }

    [Fact]
    public void GetTier_Null_ReturnsDefaultModelTier()
    {
        Assert.Equal(2, ModelTierService.GetTier(null));
    }

    [Fact]
    public void GetTier_EmptyString_ReturnsDefaultModelTier()
    {
        Assert.Equal(2, ModelTierService.GetTier(string.Empty));
    }

    [Fact]
    public void GetTier_UnknownModel_ReturnsZero()
    {
        Assert.Equal(0, ModelTierService.GetTier("gpt-4o"));
    }

    [Fact]
    public void IsAllowed_SameTier_ReturnsTrue()
    {
        Assert.True(ModelTierService.IsAllowed("claude-sonnet-4-6", "claude-sonnet-4-6"));
    }

    [Fact]
    public void IsAllowed_HigherCallerTier_ReturnsTrue()
    {
        Assert.True(ModelTierService.IsAllowed("claude-opus-4-8", "claude-sonnet-4-6"));
    }

    [Fact]
    public void IsAllowed_LowerCallerTier_ReturnsFalse()
    {
        Assert.False(ModelTierService.IsAllowed("claude-haiku-4-5-20251001", "claude-opus-4-8"));
    }

    [Fact]
    public void IsAllowed_NullCallerSpawningHaiku_ReturnsTrue()
    {
        // null caller → DefaultModelTier=2 (Sonnet), haiku=1 → allowed
        Assert.True(ModelTierService.IsAllowed(null, "claude-haiku-4-5-20251001"));
    }

    [Fact]
    public void IsAllowed_NullCallerSpawningOpus_ReturnsFalse()
    {
        // null caller → DefaultModelTier=2 (Sonnet), opus=3 → not allowed
        Assert.False(ModelTierService.IsAllowed(null, "claude-opus-4-8"));
    }

    [Theory]
    [InlineData(4, "Fable")]
    [InlineData(3, "Opus")]
    [InlineData(2, "Sonnet")]
    [InlineData(1, "Haiku")]
    [InlineData(0, "Local/Unknown")]
    [InlineData(-1, "Local/Unknown")]
    public void TierName_Tier_ReturnsExpectedName(int tier, string expected)
    {
        Assert.Equal(expected, ModelTierService.TierName(tier));
    }
}
