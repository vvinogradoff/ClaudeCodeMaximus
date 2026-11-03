using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

// <remarks>Created by Claude</remarks>
public sealed class ClaudeUsageServiceParseUsageTests
{
    [Fact]
    public void ParseUsage_BothWindowsPresent_ParsesBoth()
    {
        const string json = """
        {
            "five_hour": {"utilization": 28.0, "resets_at": "2026-11-03T09:29:59+00:00"},
            "seven_day": {"utilization": 5.0, "resets_at": "2026-11-04T05:00:00+00:00"},
            "limits": [
                {"group": "session", "severity": "normal"},
                {"group": "weekly", "severity": "warning"}
            ]
        }
        """;

        var usage = ClaudeUsageService.ParseUsage(json);

        Assert.NotNull(usage);
        Assert.Equal(28.0, usage!.FiveHourUtilization);
        Assert.Equal(5.0, usage.SevenDayUtilization);
        Assert.Equal("normal", usage.FiveHourSeverity);
        Assert.Equal("warning", usage.SevenDaySeverity);
    }

    [Fact]
    public void ParseUsage_FiveHourWindowIsJsonNull_StillParsesSevenDay()
    {
        // Plans without a 5-hour session cap (e.g. Ultra) report "five_hour": null rather than
        // omitting the key. This must not discard the otherwise-valid seven_day reading.
        const string json = """
        {
            "five_hour": null,
            "seven_day": {"utilization": 5.0, "resets_at": "2026-11-04T05:00:00+00:00"},
            "limits": [
                {"group": "weekly", "severity": "normal"}
            ]
        }
        """;

        var usage = ClaudeUsageService.ParseUsage(json);

        Assert.NotNull(usage);
        Assert.Equal(0.0, usage!.FiveHourUtilization);
        Assert.Equal(5.0, usage.SevenDayUtilization);
    }

    [Fact]
    public void ParseUsage_FiveHourKeyMissingEntirely_StillParsesSevenDay()
    {
        const string json = """
        {
            "seven_day": {"utilization": 5.0, "resets_at": "2026-11-04T05:00:00+00:00"}
        }
        """;

        var usage = ClaudeUsageService.ParseUsage(json);

        Assert.NotNull(usage);
        Assert.Equal(0.0, usage!.FiveHourUtilization);
        Assert.Equal(5.0, usage.SevenDayUtilization);
    }

    [Fact]
    public void ParseUsage_SevenDayWindowIsJsonNull_StillParsesFiveHour()
    {
        const string json = """
        {
            "five_hour": {"utilization": 28.0, "resets_at": "2026-11-03T09:29:59+00:00"},
            "seven_day": null
        }
        """;

        var usage = ClaudeUsageService.ParseUsage(json);

        Assert.NotNull(usage);
        Assert.Equal(28.0, usage!.FiveHourUtilization);
        Assert.Equal(0.0, usage.SevenDayUtilization);
    }

    [Fact]
    public void ParseUsage_NeitherWindowPresent_ReturnsNull()
    {
        const string json = "{\"unrelated\": true}";

        var usage = ClaudeUsageService.ParseUsage(json);

        Assert.Null(usage);
    }
}
