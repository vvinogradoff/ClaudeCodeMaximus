using System;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

// <remarks>Created by Claude</remarks>
public sealed class ClaudeUsageStalenessServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    private static ClaudeUsageData MakeUsage(DateTimeOffset fiveHourResetsAt, DateTimeOffset sevenDayResetsAt) =>
        new(
            FiveHourUtilization: 42.0,
            FiveHourResetsAt: fiveHourResetsAt,
            FiveHourSeverity: "warning",
            SevenDayUtilization: 4.0,
            SevenDayResetsAt: sevenDayResetsAt,
            SevenDaySeverity: "warning");

    [Fact]
    public void ZeroStaleWindows_BothWindowsFresh_ReturnsUnchanged()
    {
        var data = MakeUsage(Now.AddHours(1), Now.AddDays(3));

        var result = ClaudeUsageStalenessService.ZeroStaleWindows(data, Now);

        Assert.Equal(data, result);
    }

    [Fact]
    public void ZeroStaleWindows_FiveHourWindowExpired_ZeroesFiveHourOnly()
    {
        var data = MakeUsage(Now.AddHours(-1), Now.AddDays(3));

        var result = ClaudeUsageStalenessService.ZeroStaleWindows(data, Now);

        Assert.Equal(0, result.FiveHourUtilization);
        Assert.Equal(Constants.Usage.NormalSeverity, result.FiveHourSeverity);
        Assert.Equal(data.SevenDayUtilization, result.SevenDayUtilization);
        Assert.Equal(data.SevenDaySeverity, result.SevenDaySeverity);
    }

    [Fact]
    public void ZeroStaleWindows_SevenDayWindowExpired_ZeroesSevenDayOnly()
    {
        var data = MakeUsage(Now.AddHours(1), Now.AddDays(-1));

        var result = ClaudeUsageStalenessService.ZeroStaleWindows(data, Now);

        Assert.Equal(data.FiveHourUtilization, result.FiveHourUtilization);
        Assert.Equal(data.FiveHourSeverity, result.FiveHourSeverity);
        Assert.Equal(0, result.SevenDayUtilization);
        Assert.Equal(Constants.Usage.NormalSeverity, result.SevenDaySeverity);
    }

    [Fact]
    public void ZeroStaleWindows_BothWindowsExpired_ZeroesBoth()
    {
        var data = MakeUsage(Now.AddHours(-1), Now.AddDays(-1));

        var result = ClaudeUsageStalenessService.ZeroStaleWindows(data, Now);

        Assert.Equal(0, result.FiveHourUtilization);
        Assert.Equal(Constants.Usage.NormalSeverity, result.FiveHourSeverity);
        Assert.Equal(0, result.SevenDayUtilization);
        Assert.Equal(Constants.Usage.NormalSeverity, result.SevenDaySeverity);
    }

    [Fact]
    public void ZeroStaleWindows_ExactlyAtResetBoundary_TreatedAsStale()
    {
        var data = MakeUsage(Now, Now.AddDays(3));

        var result = ClaudeUsageStalenessService.ZeroStaleWindows(data, Now);

        Assert.Equal(0, result.FiveHourUtilization);
    }

    [Fact]
    public void IsFiveHourStale_ResetInFuture_ReturnsFalse()
    {
        var data = MakeUsage(Now.AddHours(1), Now.AddDays(3));

        Assert.False(ClaudeUsageStalenessService.IsFiveHourStale(data, Now));
    }

    [Fact]
    public void IsFiveHourStale_ResetInPast_ReturnsTrue()
    {
        var data = MakeUsage(Now.AddHours(-1), Now.AddDays(3));

        Assert.True(ClaudeUsageStalenessService.IsFiveHourStale(data, Now));
    }

    [Fact]
    public void IsSevenDayStale_ResetInFuture_ReturnsFalse()
    {
        var data = MakeUsage(Now.AddHours(1), Now.AddDays(3));

        Assert.False(ClaudeUsageStalenessService.IsSevenDayStale(data, Now));
    }

    [Fact]
    public void IsSevenDayStale_ResetInPast_ReturnsTrue()
    {
        var data = MakeUsage(Now.AddHours(1), Now.AddDays(-1));

        Assert.True(ClaudeUsageStalenessService.IsSevenDayStale(data, Now));
    }
}
