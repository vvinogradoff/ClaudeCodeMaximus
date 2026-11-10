using System.IO;
using System.Text.Json;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>The <c>Diagnostics</c> settings section and its defaults.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DiagnosticsSettingsTests
{
	private static void AssertDefaults(DiagnosticsSettingsModel d)
	{
		Assert.True(d.Enabled);
		Assert.False(d.VerboseLogging);
		Assert.Equal(1000, d.HangWarnMs);
		Assert.Equal(5000, d.HangCaptureMs);
		Assert.Equal(10, d.HangCaptureCooldownMinutes);
		Assert.Equal(60, d.MetricsIntervalSeconds);
		Assert.Equal(4096, d.MemorySpikeThresholdMb);
		Assert.False(d.DumpsEnabled);
		Assert.Equal(20, d.ArtifactRetentionCount);
		Assert.Equal(3, d.DumpRetentionCount);
	}

	[Fact]
	public void NewSettings_HaveDocumentedDefaults() => AssertDefaults(new AppSettingsModel().Diagnostics);

	[Fact]
	public void Deserialize_SectionMissing_YieldsDefaults()
	{
		var settings = JsonSerializer.Deserialize<AppSettingsModel>("""{ "ClaudePath": "claude" }""")!;

		AssertDefaults(settings.Diagnostics);
	}

	[Fact]
	public void Deserialize_PartialSection_KeepsDefaultsForTheRest()
	{
		var settings = JsonSerializer.Deserialize<AppSettingsModel>("""{ "Diagnostics": { "HangWarnMs": 2500, "DumpsEnabled": true } }""")!;

		Assert.Equal(2500, settings.Diagnostics.HangWarnMs);
		Assert.True(settings.Diagnostics.DumpsEnabled);
		Assert.Equal(5000, settings.Diagnostics.HangCaptureMs);
		Assert.True(settings.Diagnostics.Enabled);
	}

	[Fact]
	public void Load_ExistingFileWithoutSection_YieldsDefaults()
	{
		var dir = Path.Combine(Path.GetTempPath(), $"diag-settings-{System.Guid.NewGuid():N}");
		Directory.CreateDirectory(dir);
		try
		{
			File.WriteAllText(Path.Combine(dir, Constants.SettingsFileName), """{ "ClaudePath": "claude", "Theme": "Light" }""");
			var service = new AppSettingsService(dir);

			service.Load();

			AssertDefaults(service.Settings.Diagnostics);
			Assert.Equal("Light", service.Settings.Theme);
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
		}
	}
}
