using System.Windows.Input;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The W10a commands and slot, and the key bindings the Avalonia import carries into them.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ChromeModuleTests
{
	private sealed class FixedProbe : ISystemThemeProbe
	{
		public bool IsLight => true;

		public event EventHandler? Changed
		{
			add { }
			remove { }
		}
	}

	private readonly ClientSettingsStore _store = new(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
	private readonly ShellCommands _commands;
	private readonly ShellSlots _slots = new();
	private readonly AppearanceService _appearance;
	private readonly TitleBarViewModel _titleBar;

	public ChromeModuleTests()
	{
		_commands = new ShellCommands(_store, NullLogger<ShellCommands>.Instance);
		_appearance = new AppearanceService(_store, new FixedProbe());
		var ui = new ImmediateUiDispatcher();
		_titleBar = new TitleBarViewModel(_appearance, _store, _commands, ui, "1", false);
		var connection = new ConnectionViewModel(new ScriptedLocator(), new FixedClientFactory(new FakeTessynClient()), ui, NullLogger<ConnectionViewModel>.Instance);
		var main = new MainViewModel(connection, _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException(), new MemoryClientState(), NullLogger<MainViewModel>.Instance);
		var bar = new StatusBarViewModel(connection, main, ui, NullLogger<StatusBarViewModel>.Instance);
		new ChromeShellModule(bar, _titleBar, main, _appearance).Register(_slots, _commands);
	}

	[Fact]
	public void TheModule_FillsTheStatusBarSlot_AndRegistersTheThreeCommands()
	{
		var slot = Assert.Single(_slots.For(Constants.Slots.StatusBarItems));
		Assert.Equal(Constants.StatusBar.ItemOrder, slot.Order);
		Assert.NotNull(_commands.Find(Constants.Appearance.ToggleThemeCommandId));
		Assert.NotNull(_commands.Find(Constants.Appearance.ToggleTreeCommandId));
		Assert.Equal("Ctrl+Enter", _commands.Find(Constants.Appearance.SendCommandId)?.DefaultGesture);
	}

	[Fact]
	public async Task ToggleCommands_ChangeTheThemeAndThePanel()
	{
		await _commands.ExecuteAsync(Constants.Appearance.ToggleThemeCommandId, null);
		Assert.True(_appearance.IsDark);

		await _commands.ExecuteAsync(Constants.Appearance.ToggleTreeCommandId, null);
		Assert.False(_titleBar.IsTreeVisible);
	}

	[Fact]
	public void CtrlEnter_IsTheSendGesture_ButWithoutASessionTheKeyStaysWithTheTextBox()
	{
		Assert.Equal(Constants.Appearance.SendCommandId, _commands.FindByGesture(Key.Enter, ModifierKeys.Control)?.Id);

		var taken = _commands.TryExecuteGesture(Key.Enter, ModifierKeys.Control, textInputFocused: true);

		Assert.False(taken);   // nothing to send: Ctrl+Enter is left alone
	}

	[Fact]
	public void SendBinding_OfTheAvaloniaClient_IsCarriedOver_WithCmdBecomingCtrl()
	{
		var importer = new AvaloniaImporter(_store, _commands);
		var result = importer.Apply(Dto(new KeyBindingDto("Send", "Cmd+Enter")));

		Assert.DoesNotContain("Send", result.SkippedBindings);
		Assert.Equal(Constants.Appearance.SendCommandId, _commands.FindByGesture(Key.Enter, ModifierKeys.Control)?.Id);
		Assert.DoesNotContain(Constants.Appearance.SendCommandId, _store.Get<KeyBindingSettings>().Gestures.Keys);   // same as the default: no override stored
	}

	[Fact]
	public void SendBinding_WithAnotherCombination_RebindsTheCommand()
	{
		new AvaloniaImporter(_store, _commands).Apply(Dto(new KeyBindingDto("Send", "Ctrl+Shift+Enter")));

		Assert.Equal(Constants.Appearance.SendCommandId, _commands.FindByGesture(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift)?.Id);
		Assert.Null(_commands.FindByGesture(Key.Enter, ModifierKeys.Control));
	}

	[Fact]
	public void TheImport_AppliesThemeFontsAndScreensaverToWhatTheWindowReads()
	{
		new AvaloniaImporter(_store, _commands).Apply(Dto());

		Assert.True(_appearance.IsDark);
		Assert.Equal(15, _appearance.Current.AssistantFontSize);
		Assert.Equal(16, _appearance.Current.InputFontSize);
		var saver = _store.Get<ScreensaverSettings>();
		Assert.Equal((@"D:\pictures", 300, 20), (saver.Directory, saver.TimeoutSeconds, saver.SlideshowIntervalSeconds));
	}

	private static AvaloniaUiSettingsDto Dto(params KeyBindingDto[] bindings)
	{
		var colors = new ThemeColorsDto("#111111", "#eeeeee", "#222222", "#ffffff", "#333333", "#dddddd", "#444444", "#cccccc", "#555555", "#666666", "#777777", "#888888");
		return new AvaloniaUiSettingsDto("Dark", 15, 14, 12, 16, colors, colors, bindings, new ScreensaverSettingsDto(@"D:\pictures", 300, 20));
	}
}
