using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels.Settings;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.6.23.7, FR.6.23.8 / TC.DS.39, TC.DS.40: the Session command (Ctrl+1) and the Focus command (Ctrl+2) in the registry, their keys in Settings → Keyboard shortcuts, the tooltips that name the
/// key in effect, and the docking area bringing the Session view forward over a tabbed panel without taking the keyboard focus.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusHeaderShortcutTests
{
	private static readonly MonitorBounds Screen = new(0, 0, 1920, 1080);

	private readonly FakeShellLayout _layout = new();
	private readonly ClientSettingsStore _store = new(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
	private readonly ShellCommands _commands;

	public FocusHeaderShortcutTests()
	{
		_commands = new ShellCommands(_store, NullLogger<ShellCommands>.Instance);
		new ShellLayoutModule(_layout, new NoDialogs()).Register(new ShellSlots(), _commands);
	}

	private sealed class NoDialogs : IDialogService
	{
		public void CancelPending()
		{
		}

		public Task<bool> ConfirmAsync(ConfirmDialogRequest request) => Task.FromResult(false);

		public Task<string?> InputAsync(InputDialogRequest request) => Task.FromResult<string?>(null);

		public Task<string?> ChoiceAsync(ChoiceDialogRequest request) => Task.FromResult<string?>(null);
	}

	[Fact]
	public void TheSessionCommand_IsRegisteredWithCtrl1_AllowedInTextInputs_AndNotInThePanelsMenu()
	{
		var command = _commands.Find(Constants.ShellLayout.ShowSessionCommandId);

		Assert.NotNull(command);
		Assert.Equal("view.showSession", command.Id);
		Assert.Equal("Session", command.Title);
		Assert.Equal("Ctrl+1", _commands.GetGesture(Constants.ShellLayout.ShowSessionCommandId));
		Assert.True(command.AllowInTextInput);
		Assert.DoesNotContain(_commands.BuildMenu(Constants.Menus.Main, null), m => m.CommandId == Constants.ShellLayout.ShowSessionCommandId);
		Assert.Contains(_commands.BuildMenu(Constants.Menus.Main, null), m => m.CommandId == Constants.ShellLayout.ShowFocusCommandId);   // Focus stays in the menu
		Assert.Equal("Ctrl+2", _commands.GetGesture(Constants.ShellLayout.ShowFocusCommandId));
		Assert.True(_commands.Find(Constants.ShellLayout.ShowFocusCommandId)!.AllowInTextInput);
	}

	[Fact]
	public void TheKeys_BringTheSessionViewAndTheFocusPanelForward_AlsoFromATextInput()
	{
		Assert.True(_commands.TryExecuteGesture(Key.D1, ModifierKeys.Control, textInputFocused: true));
		Assert.True(_commands.TryExecuteGesture(Key.D2, ModifierKeys.Control, textInputFocused: true));
		Assert.True(_commands.TryExecuteGesture(Key.D1, ModifierKeys.Control, textInputFocused: false));

		Assert.Equal([Constants.DockedShell.SessionPanelId, Constants.DockedShell.FocusPanelId, Constants.DockedShell.SessionPanelId], _layout.BroughtForward);
	}

	[Fact]
	public void SettingsKeyboardShortcuts_ListsSessionAndFocusWithTheirDefaults_AndARebindingWorks()
	{
		var keyboard = new KeyboardSectionViewModel(_store, _commands);

		Assert.Equal("Ctrl+1", keyboard.Rows.Single(r => r.CommandId == Constants.ShellLayout.ShowSessionCommandId).Text);
		Assert.Equal("Session", keyboard.Rows.Single(r => r.CommandId == Constants.ShellLayout.ShowSessionCommandId).Title);
		Assert.Equal("Ctrl+2", keyboard.Rows.Single(r => r.CommandId == Constants.ShellLayout.ShowFocusCommandId).Text);

		keyboard.Rows.Single(r => r.CommandId == Constants.ShellLayout.ShowSessionCommandId).Text = "Ctrl+Shift+S";
		Assert.True(keyboard.SaveAsync().GetAwaiter().GetResult());

		Assert.Equal("Ctrl+Shift+S", _commands.GetGesture(Constants.ShellLayout.ShowSessionCommandId));
		Assert.False(_commands.TryExecuteGesture(Key.D1, ModifierKeys.Control, false));
		Assert.True(_commands.TryExecuteGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift, false));
		Assert.Equal([Constants.DockedShell.SessionPanelId], _layout.BroughtForward);
	}

	[Fact]
	public void TheTooltips_NameTheKeysInEffect_AndFollowARebindingOrAnUnbinding()
	{
		var hints = new ShellButtonHints(_commands, _store);
		var changed = new List<string?>();
		hints.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

		Assert.Equal("Show the session (Ctrl+1)", hints.SessionToolTip);
		Assert.Equal("Show what needs your attention (Ctrl+2)", hints.FocusToolTip);

		_store.Update<KeyBindingSettings>(k =>
		{
			k.Gestures[Constants.ShellLayout.ShowSessionCommandId] = "Ctrl+Shift+S";
			k.Gestures[Constants.ShellLayout.ShowFocusCommandId] = string.Empty;
		});

		Assert.Equal("Show the session (Ctrl+Shift+S)", hints.SessionToolTip);
		Assert.Equal("Show what needs your attention", hints.FocusToolTip);
		Assert.Contains(nameof(ShellButtonHints.SessionToolTip), changed);
		Assert.Contains(nameof(ShellButtonHints.FocusToolTip), changed);
		Assert.Equal("Show the session", new ShellButtonHints().SessionToolTip);   // no registry: no key
	}

	private static (DockingArea Area, Window Window, TextBox Box) Start()
	{
		var box = new TextBox { Name = "Composer", MinWidth = 100 };
		var area = new DockingArea(box);
		var window = new Window { Content = area, Width = 1400, Height = 800, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
		var state = new MemoryClientState();
		area.BuildShell(
			[
				new ShellPanelDefinition { Id = Constants.DockedShell.SessionsPanelId, Title = "Sessions", Content = new TextBlock { Text = "s" }, IsLeft = true },
				new ShellPanelDefinition { Id = Constants.DockedShell.FocusPanelId, Title = "Focus", Content = new TextBlock { Text = "f" } },
			],
			new ShellLayoutMemory(new ClientSettingsStore(state, NullLogger<ClientSettingsStore>.Instance), state, true), NullLogger.Instance, () => ([Screen], Screen));
		window.Show();
		Settle(window);
		return (area, window, box);
	}

	private static void Settle(Window window)
	{
		window.UpdateLayout();
		window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
		window.UpdateLayout();
	}

	[Fact]
	public void TheSessionButton_SelectsTheSessionTab_WhenFocusIsTabbedOverIt_AndLeavesTheKeyboardFocusAlone()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window, _) = Start();
			var ops = (IDockingTestOperations)area;
			Assert.True(ops.Tab(Constants.DockedShell.FocusPanelId, Constants.DockedShell.SessionPanelId).Ok);
			area.BringForward(Constants.DockedShell.FocusPanelId);
			Settle(window);
			var session = ops.State().Single(p => p.Id == Constants.DockedShell.SessionPanelId);
			var focus = ops.State().Single(p => p.Id == Constants.DockedShell.FocusPanelId);
			Assert.False(session.Selected);   // Focus is the selected tab of the session area
			Assert.True(focus.Selected);

			var focused = Keyboard.FocusedElement;
			var active = area.DocumentPane.Root.ActiveContent;
			area.BringForward(Constants.DockedShell.SessionPanelId);
			Settle(window);

			Assert.True(ops.State().Single(p => p.Id == Constants.DockedShell.SessionPanelId).Selected);
			Assert.False(ops.State().Single(p => p.Id == Constants.DockedShell.FocusPanelId).Selected);
			Assert.Same(focused, Keyboard.FocusedElement);
			Assert.Same(active, area.DocumentPane.Root.ActiveContent);

			// and the Focus command goes back
			area.BringForward(Constants.DockedShell.FocusPanelId);
			Settle(window);
			Assert.True(ops.State().Single(p => p.Id == Constants.DockedShell.FocusPanelId).Selected);
			window.Close();
		});
	}

	[Fact]
	public void TheSessionButton_LeavesTheSessionViewShown_WhenFocusFloats_AndRestoresAMinimizedWindow()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window, _) = Start();
			var ops = (IDockingTestOperations)area;
			Assert.True(ops.Float(Constants.DockedShell.FocusPanelId, null).Ok);
			Settle(window);

			area.BringForward(Constants.DockedShell.SessionPanelId);
			Settle(window);
			Assert.True(ops.State().Single(p => p.Id == Constants.DockedShell.SessionPanelId).Selected);
			Assert.True(ops.State().Single(p => p.Id == Constants.DockedShell.FocusPanelId).Floating);

			window.WindowState = WindowState.Minimized;
			area.BringForward(Constants.DockedShell.SessionPanelId);
			Assert.Equal(WindowState.Normal, window.WindowState);
			window.Close();
		});
	}
}
