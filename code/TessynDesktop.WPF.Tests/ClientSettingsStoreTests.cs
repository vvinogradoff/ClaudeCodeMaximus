using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>The typed, versioned settings sections inside wpf.json (W0).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClientSettingsStoreTests : IDisposable
{
	[ClientSettingsSection("testSection", version: 2)]
	public sealed class TestSection
	{
		public string Name { get; set; } = "default";

		public int Count { get; set; } = 7;
	}

	public sealed class UnmarkedSection
	{
	}

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-settings-" + Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		TestDirectory.Delete(_root);
	}

	private JsonClientStateStore CreateState() =>
		new(new AppPaths(_root), NullLogger<JsonClientStateStore>.Instance, TimeSpan.FromMilliseconds(20));

	private static ClientSettingsStore CreateSettings(JsonClientStateStore state) => new(state, NullLogger<ClientSettingsStore>.Instance);

	[Fact]
	public async Task NeverWrittenSection_YieldsDefaults()
	{
		var state = CreateState();
		await state.LoadAsync();

		var appearance = CreateSettings(state).Get<AppearanceSettings>();

		Assert.Equal(ClientTheme.System, appearance.Theme);
		Assert.Equal(Constants.Defaults.FontSize, appearance.InputFontSize);
	}

	[Fact]
	public async Task Update_RoundTripsThroughTheFile()
	{
		var first = CreateState();
		await first.LoadAsync();
		var settings = CreateSettings(first);
		settings.Update<AppearanceSettings>(a =>
		{
			a.Theme = ClientTheme.Dark;
			a.UserFontSize = 16;
		});
		settings.Update<KeyBindingSettings>(k => k.Gestures["tree.rename"] = "F2");
		settings.Update<ScreensaverSettings>(s => s.Directory = @"C:\pics");
		await first.FlushAsync();

		var second = CreateState();
		await second.LoadAsync();
		var reread = CreateSettings(second);

		Assert.Equal(ClientTheme.Dark, reread.Get<AppearanceSettings>().Theme);
		Assert.Equal(16, reread.Get<AppearanceSettings>().UserFontSize);
		Assert.Equal("F2", reread.Get<KeyBindingSettings>().Gestures["tree.rename"]);
		Assert.Equal(@"C:\pics", reread.Get<ScreensaverSettings>().Directory);
		Assert.Equal(Constants.Defaults.ScreensaverTimeoutSeconds, reread.Get<ScreensaverSettings>().TimeoutSeconds);
		Assert.Contains("\"theme\": \"Dark\"", File.ReadAllText(new AppPaths(_root).ClientStateFilePath));
	}

	[Fact]
	public async Task Sections_AreIndependentOfEachOtherAndOfTheRestOfTheState()
	{
		var state = CreateState();
		await state.LoadAsync();
		state.Update(s => s.TreeWidth = 400);
		var settings = CreateSettings(state);

		settings.Update<TestSection>(t => t.Count = 1);
		settings.Update<AppearanceSettings>(a => a.Theme = ClientTheme.Light);

		Assert.Equal(1, settings.Get<TestSection>().Count);
		Assert.Equal(ClientTheme.Light, settings.Get<AppearanceSettings>().Theme);
		Assert.Equal(400, state.State.TreeWidth);
	}

	[Fact]
	public async Task Get_ReturnsACopy_SoUnsavedChangesNeverLeak()
	{
		var state = CreateState();
		await state.LoadAsync();
		var settings = CreateSettings(state);
		settings.Update<TestSection>(t => t.Name = "saved");

		var copy = settings.Get<TestSection>();
		copy.Name = "scribbled";

		Assert.Equal("saved", settings.Get<TestSection>().Name);
	}

	[Fact]
	public async Task Update_StoresTheSectionVersion()
	{
		var state = CreateState();
		await state.LoadAsync();

		CreateSettings(state).Update<TestSection>(t => t.Count = 3);

		Assert.Equal(2, state.State.Sections["testSection"].Version);
	}

	[Fact]
	public async Task SectionFromANewerVersion_ReadsAsDefaults_AndIsKeptUntilWritten()
	{
		var state = CreateState();
		await state.LoadAsync();
		state.State.Sections["testSection"] = new ClientSectionModel { Version = 3, Data = JsonDocument.Parse("""{"name":"future","count":99}""").RootElement.Clone() };
		var settings = CreateSettings(state);

		Assert.Equal("default", settings.Get<TestSection>().Name);
		Assert.Equal(3, state.State.Sections["testSection"].Version);

		settings.Update<TestSection>(t => t.Count = 1);

		Assert.Equal(2, state.State.Sections["testSection"].Version);
	}

	[Fact]
	public async Task OlderSection_IsReadAndMissingFieldsTakeDefaults()
	{
		var state = CreateState();
		await state.LoadAsync();
		state.State.Sections["testSection"] = new ClientSectionModel { Version = 1, Data = JsonDocument.Parse("""{"name":"old"}""").RootElement.Clone() };

		var section = CreateSettings(state).Get<TestSection>();

		Assert.Equal("old", section.Name);
		Assert.Equal(7, section.Count);
	}

	[Fact]
	public async Task UnreadableSection_YieldsDefaults()
	{
		var state = CreateState();
		await state.LoadAsync();
		state.State.Sections["testSection"] = new ClientSectionModel { Version = 2, Data = JsonDocument.Parse("""{"name":42}""").RootElement.Clone() };

		Assert.Equal("default", CreateSettings(state).Get<TestSection>().Name);
	}

	[Fact]
	public async Task Update_RaisesChangedWithTheSectionName()
	{
		var state = CreateState();
		await state.LoadAsync();
		var settings = CreateSettings(state);
		var raised = new List<string>();
		settings.Changed += (_, e) => raised.Add(e.Section);

		settings.Update<KeyBindingSettings>(k => k.Gestures["a"] = "F2");
		settings.Update<ScreensaverSettings>(s => s.TimeoutSeconds = 0);

		Assert.Equal([Constants.SettingsSections.KeyBindings, Constants.SettingsSections.Screensaver], raised);
	}

	[Fact]
	public async Task ATypeWithoutTheSectionAttribute_IsAProgrammingError()
	{
		var state = CreateState();
		await state.LoadAsync();

		Assert.Throws<InvalidOperationException>(() => CreateSettings(state).Get<UnmarkedSection>());
	}

	[Fact]
	public async Task StateFileWithoutOrWithNullSections_LoadsWithNoSections()
	{
		var paths = new AppPaths(_root);
		Directory.CreateDirectory(Path.GetDirectoryName(paths.ClientStateFilePath)!);
		File.WriteAllText(paths.ClientStateFilePath, """{"version":1,"treeWidth":300,"sections":null}""");
		var state = CreateState();

		await state.LoadAsync();

		Assert.NotNull(state.State.Sections);
		Assert.Equal(ClientTheme.System, CreateSettings(state).Get<AppearanceSettings>().Theme);
	}
}
