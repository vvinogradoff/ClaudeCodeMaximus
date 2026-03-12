using System.IO;
using System.Text.Json;
using ClaudeMaximus.Models;
using ClaudeMaximus.Services;
using Xunit;

namespace ClaudeMaximus.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class AppSettingsServiceTests
{
	[Fact]
	public void Save_ThenLoad_RoundTripsTreeAndSettings()
	{
		var tempDir = CreateTempDir();
		try
		{
			var service = new AppSettingsService(tempDir);

			service.Settings.ClaudePath = "/usr/local/bin/claude";
			service.Settings.SessionFilesRoot = Path.Combine(tempDir, "sessions");
			service.Settings.Tree.Add(new DirectoryNodeModel
			{
				Path = @"C:\Projects\datum_api",
				Sessions =
				[
					new SessionNodeModel { Name = "Auth spike", FileName = "2026-03-12-1430-xkqbzf.txt" }
				],
			});

			service.Save();
			service.Load();

			Assert.Equal("/usr/local/bin/claude", service.Settings.ClaudePath);
			Assert.Single(service.Settings.Tree);
			Assert.Equal(@"C:\Projects\datum_api", service.Settings.Tree[0].Path);
			Assert.Single(service.Settings.Tree[0].Sessions);
			Assert.Equal("Auth spike", service.Settings.Tree[0].Sessions[0].Name);
			Assert.Equal("2026-03-12-1430-xkqbzf.txt", service.Settings.Tree[0].Sessions[0].FileName);
		}
		finally
		{
			Directory.Delete(tempDir, recursive: true);
		}
	}

	[Fact]
	public void Save_WritesAtomically_ResultFileIsValidJson()
	{
		var tempDir = CreateTempDir();
		try
		{
			var service = new AppSettingsService(tempDir);
			service.Settings.ClaudePath = "claude";
			service.Save();

			var settingsPath = Path.Combine(tempDir, ClaudeMaximus.Constants.SettingsFileName);
			Assert.True(File.Exists(settingsPath));

			var json = File.ReadAllText(settingsPath);
			var parsed = JsonSerializer.Deserialize<AppSettingsModel>(json);
			Assert.NotNull(parsed);
		}
		finally
		{
			Directory.Delete(tempDir, recursive: true);
		}
	}

	[Fact]
	public void Load_WhenNoFileExists_UsesDefaults()
	{
		var tempDir = CreateTempDir();
		try
		{
			var service = new AppSettingsService(tempDir);
			service.Load();

			Assert.Equal("claude", service.Settings.ClaudePath);
			Assert.Empty(service.Settings.Tree);
		}
		finally
		{
			Directory.Delete(tempDir, recursive: true);
		}
	}

	private static string CreateTempDir()
	{
		var path = Path.Combine(Path.GetTempPath(), "cm_test_settings_" + Path.GetRandomFileName());
		Directory.CreateDirectory(path);
		return path;
	}
}
