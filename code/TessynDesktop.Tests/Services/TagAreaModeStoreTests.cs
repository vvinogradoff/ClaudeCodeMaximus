using System;
using System.IO;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>The global tag area mode is remembered in the app settings (FR.24.16).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagAreaModeStoreTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "tag-mode-" + Guid.NewGuid().ToString("N")[..8]);

	public TagAreaModeStoreTests() => Directory.CreateDirectory(_dir);

	public void Dispose() => Directory.Delete(_dir, recursive: true);

	[Fact]
	public void DefaultsToMini() =>
		Assert.False(new TagAreaModeStore(new AppSettingsService(_dir)).IsExpanded);

	[Fact]
	public void TheMode_SurvivesARestart()
	{
		new TagAreaModeStore(new AppSettingsService(_dir)).Set(true);

		var restarted = new AppSettingsService(_dir);
		restarted.Load();
		Assert.True(new TagAreaModeStore(restarted).IsExpanded);

		new TagAreaModeStore(restarted).Set(false);

		var again = new AppSettingsService(_dir);
		again.Load();
		Assert.False(new TagAreaModeStore(again).IsExpanded);
	}

	[Fact]
	public void ChangedFiresOnlyForAnActualChange()
	{
		var store = new TagAreaModeStore(new AppSettingsService(_dir));
		var raised = 0;
		store.Changed += () => raised++;

		store.Set(false);
		store.Set(true);
		store.Set(true);
		store.Set(false);

		Assert.Equal(2, raised);
	}

	[Fact]
	public void TwoStores_ShareTheSameSettings()
	{
		var settings = new AppSettingsService(_dir);
		var a = new TagAreaModeStore(settings);
		var b = new TagAreaModeStore(settings);

		a.Set(true);

		Assert.True(b.IsExpanded);
	}
}
