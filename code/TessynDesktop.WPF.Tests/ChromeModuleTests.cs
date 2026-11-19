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
		_titleBar = new TitleBarViewModel(_appearance, _store, _commands, new FakeShellLayout(), ui, "1", false);
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
}
