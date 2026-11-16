using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Switch;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The one-time import of the Avalonia UI settings through the read-only <c>avalonia-ui</c> section (A23 Q8).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AvaloniaImportTests : IDisposable
{
	private sealed class ScriptedDialogs : IDialogService
	{
		public List<ConfirmDialogRequest> Confirms { get; } = [];

		public bool Answer { get; set; } = true;

		public void CancelPending()
		{
		}

		public Task? Gate { get; set; }

		public async Task<bool> ConfirmAsync(ConfirmDialogRequest request)
		{
			Confirms.Add(request);
			if (Gate != null)
				await Gate;

			return Answer;
		}

		public Task<string?> InputAsync(InputDialogRequest request) => Task.FromResult<string?>(null);

		public Task<string?> ChoiceAsync(ChoiceDialogRequest request) => Task.FromResult<string?>(null);
	}

	private static readonly ThemeColorsDto Colors = new("#111111", "#eeeeee", "#222222", "#ffffff", "#333333", "#dddddd", "#444444", "#cccccc", "#555555", "#666666", "#777777", "#888888");

	private static readonly AvaloniaUiSettingsDto Avalonia = new(
		"Dark", 15, 14, 12, 16, Colors, Colors,
		[new KeyBindingDto("AddDirectory", "Cmd+Shift+N, Ctrl+Shift+N"), new KeyBindingDto("ImportSessions", "Cmd+I"), new KeyBindingDto("Send", "Ctrl+Enter")],
		new ScreensaverSettingsDto(@"D:\pictures", 300, 20));

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-switch-import-" + Guid.NewGuid().ToString("N"));
	private readonly ScriptedDialogs _dialogs = new();
	private readonly FakeTessynClient _client = new();
	private readonly JsonClientStateStore _state;
	private readonly ClientSettingsStore _settings;
	private readonly AvaloniaImportHost _host;

	public AvaloniaImportTests()
	{
		_state = new JsonClientStateStore(new AppPaths(_root), NullLogger<JsonClientStateStore>.Instance, TimeSpan.FromMilliseconds(20));
		_state.LoadAsync().GetAwaiter().GetResult();
		_settings = new ClientSettingsStore(_state, NullLogger<ClientSettingsStore>.Instance);
		_client.SettingsValues[RemoteProtocol.SettingsApi.AvaloniaUiSection] = Avalonia;
		var connection = new ConnectionViewModel(new ScriptedLocator(), new FixedClientFactory(_client), new ImmediateUiDispatcher(), NullLogger<ConnectionViewModel>.Instance);
		_host = new AvaloniaImportHost(connection, _settings, _dialogs, NullLogger<AvaloniaImportHost>.Instance);
	}

	public void Dispose()
	{
		TestDirectory.Delete(_root);
	}

	[Fact]
	public async Task Confirmed_ImportsThemeFontsScreensaverAndTheMappedKeys()
	{
		var imported = await _host.RunAsync(_client);

		Assert.True(imported);
		var appearance = _settings.Get<AppearanceSettings>();
		Assert.Equal(ClientTheme.Dark, appearance.Theme);
		Assert.Equal(15, appearance.AssistantFontSize);
		Assert.Equal(14, appearance.AssistantMarkdownFontSize);
		Assert.Equal(12, appearance.UserFontSize);
		Assert.Equal(16, appearance.InputFontSize);
		var screensaver = _settings.Get<ScreensaverSettings>();
		Assert.Equal(@"D:\pictures", screensaver.Directory);
		Assert.Equal(300, screensaver.TimeoutSeconds);
		Assert.Equal(20, screensaver.SlideshowIntervalSeconds);
		var keys = _settings.Get<KeyBindingSettings>().Gestures;
		Assert.Equal("Ctrl+Shift+N", keys["tree.addDirectory"]);
		Assert.Equal("Ctrl+Enter", keys[Constants.Appearance.SendCommandId]);   // W10a: Send has a command now
		Assert.Equal(2, keys.Count); // ImportSessions has no WPF command until the import picker (W7)
		Assert.True(_settings.Get<ImportState>().AvaloniaUiHandled);
		Assert.Equal("imported", _settings.Get<ImportState>().AvaloniaUiOutcome);
		Assert.Single(_dialogs.Confirms);
	}

	[Fact]
	public async Task AReconnectWhileTheDialogIsOpen_DoesNotOpenASecondDialog()
	{
		// L1: ClientReady fires again on a reconnect; the open dialog must not be asked twice.
		var gate = new TaskCompletionSource();
		_dialogs.Gate = gate.Task;

		var first = _host.RunAsync(_client);
		var second = await _host.RunAsync(_client);
		gate.SetResult();

		Assert.False(second);
		Assert.True(await first);
		Assert.Single(_dialogs.Confirms);
	}

	[Fact]
	public async Task Declined_ImportsNothingAndIsNotAskedAgain()
	{
		_dialogs.Answer = false;

		var imported = await _host.RunAsync(_client);
		var again = await _host.RunAsync(_client);

		Assert.False(imported);
		Assert.False(again);
		Assert.Equal(ClientTheme.System, _settings.Get<AppearanceSettings>().Theme);
		Assert.Equal("declined", _settings.Get<ImportState>().AvaloniaUiOutcome);
		Assert.Single(_dialogs.Confirms);
		Assert.DoesNotContain("settings.get:avalonia-ui", _client.Calls);
	}

	[Fact]
	public async Task AlreadyImported_NeverAsksAgain()
	{
		await _host.RunAsync(_client);
		_dialogs.Confirms.Clear();

		Assert.False(await _host.RunAsync(_client));

		Assert.Empty(_dialogs.Confirms);
	}

	[Fact]
	public async Task ADaemonWithoutTheSection_IsLeftForALaterStart()
	{
		_client.SettingsSections = [new(RemoteProtocol.SettingsApi.GeneralSection, 1)];

		Assert.False(await _host.RunAsync(_client));

		Assert.Empty(_dialogs.Confirms);
		Assert.False(_settings.Get<ImportState>().AvaloniaUiHandled);
	}

	[Fact]
	public async Task AFailedRead_IsLoggedNotRaised_AndTheQuestionStaysOpen()
	{
		_client.SettingsValues.Remove(RemoteProtocol.SettingsApi.AvaloniaUiSection);

		Assert.False(await _host.RunAsync(_client));

		Assert.False(_settings.Get<ImportState>().AvaloniaUiHandled);
	}

	[Theory]
	[InlineData("Dark", ClientTheme.Dark)]
	[InlineData(" light ", ClientTheme.Light)]
	[InlineData("Default", ClientTheme.System)]
	[InlineData(null, ClientTheme.System)]
	public void ParseTheme_MapsTheAvaloniaNames(string? name, ClientTheme expected)
	{
		Assert.Equal(expected, AvaloniaUiImporter.ParseTheme(name));
	}

	[Theory]
	[InlineData("Cmd+Shift+N, Ctrl+Shift+N", "Ctrl+Shift+N")]
	[InlineData("Meta+O", "Ctrl+O")]
	[InlineData("F2", "F2")]
	[InlineData("", null)]
	public void ToGesture_TakesTheFirstCombinationInWpfNotation(string binding, string? expected)
	{
		Assert.Equal(expected, AvaloniaUiImporter.ToGesture(binding));
	}
}
