using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.6.14, FR.6.19: the Panels group of the hamburger menu (entries with a check mark, Reset layout that asks first) and the null-safe layout host.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellLayoutModuleTests
{
	private sealed class Dialogs : IDialogService
	{
		public List<ConfirmDialogRequest> Confirms { get; } = [];

		public bool Answer { get; set; } = true;

		public void CancelPending()
		{
		}

		public Task<bool> ConfirmAsync(ConfirmDialogRequest request)
		{
			Confirms.Add(request);
			return Task.FromResult(Answer);
		}

		public Task<string?> InputAsync(InputDialogRequest request) => Task.FromResult<string?>(null);

		public Task<string?> ChoiceAsync(ChoiceDialogRequest request) => Task.FromResult<string?>(null);
	}

	private readonly FakeShellLayout _layout = new();
	private readonly Dialogs _dialogs = new();
	private readonly ShellCommands _commands = new(new ClientSettingsStore(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance), NullLogger<ShellCommands>.Instance);

	public ShellLayoutModuleTests()
	{
		// An earlier entry of the menu (Settings, order 900) so that the group's place after the registered entries shows.
		_commands.Register(new ShellCommand { Id = "earlier", Title = "Settings", ExecuteAsync = _ => Task.CompletedTask });
		_commands.AddMenuEntry(Constants.Menus.Main, "earlier", Constants.Settings.OpenButtonOrder);
		new ShellLayoutModule(_layout, _dialogs).Register(new ShellSlots(), _commands);
	}

	[Fact]
	public void ThePanelsGroup_ComesAfterTheRegisteredEntries_WithOneEntryPerClosablePanel_AndResetLayoutLast()
	{
		var menu = _commands.BuildMenu(Constants.Menus.Main, null);

		Assert.Equal(["Settings", "Sessions", "Recent", "Tags", "Focus", "Reset layout…"], menu.Select(m => m.Title));
		Assert.True(menu[1].BeginsGroup);   // a separator before the group; Exit follows after the title bar's own separator
		Assert.False(menu[2].BeginsGroup);
		Assert.All(menu.Skip(1), m => Assert.True(m.IsEnabled));
	}

	[Fact]
	public async Task AnEntry_ShowsACheckMarkWhilePanelIsOpen_AndPickingItBringsThePanelForward()
	{
		Assert.All(_commands.BuildMenu(Constants.Menus.Main, null).Skip(1).Take(4), m => Assert.True(m.IsChecked));
		Assert.False(_commands.BuildMenu(Constants.Menus.Main, null)[0].IsChecked);

		_layout.Close(Constants.DockedShell.RecentPanelId);
		Assert.False(_commands.BuildMenu(Constants.Menus.Main, null).Single(m => m.Title == "Recent").IsChecked);

		await _commands.ExecuteAsync(Constants.ShellLayout.ShowRecentCommandId, null);
		Assert.Equal([Constants.DockedShell.RecentPanelId], _layout.BroughtForward);
		Assert.True(_commands.BuildMenu(Constants.Menus.Main, null).Single(m => m.Title == "Recent").IsChecked);

		await _commands.ExecuteAsync(Constants.ShellLayout.ShowFocusCommandId, null);
		Assert.Equal(Constants.DockedShell.FocusPanelId, _layout.BroughtForward.Last());   // an open panel is brought forward too
	}

	[Fact]
	public async Task ResetLayout_AsksFirst_AndResetsOnlyWhenConfirmed()
	{
		_dialogs.Answer = false;
		await _commands.ExecuteAsync(Constants.ShellLayout.ResetCommandId, null);
		Assert.Equal(0, _layout.Resets);

		_dialogs.Answer = true;
		await _commands.ExecuteAsync(Constants.ShellLayout.ResetCommandId, null);
		Assert.Equal(1, _layout.Resets);
		Assert.All(_dialogs.Confirms, c => Assert.Equal("Reset the window layout? Panels you moved, floated or closed go back to their default places.", c.Message));
		Assert.Equal(2, _dialogs.Confirms.Count);
	}

	[Fact]
	public void TheHost_AnswersNothingOpenBeforeTheWindowExists_AndForwardsAfterwards()
	{
		var host = new ShellLayoutHost();
		var raised = 0;
		host.Changed += (_, _) => raised++;

		Assert.False(host.IsOpen(Constants.DockedShell.FocusPanelId));
		Assert.False(host.CanToggleAutoHide(Constants.DockedShell.SessionsPanelId));
		host.BringForward(Constants.DockedShell.FocusPanelId);   // no window yet: nothing happens, nothing throws
		host.ResetLayout();

		host.Register(_layout);
		Assert.Equal(1, raised);
		Assert.True(host.IsOpen(Constants.DockedShell.FocusPanelId));
		host.ToggleAutoHide(Constants.DockedShell.SessionsPanelId);
		Assert.True(host.IsAutoHidden(Constants.DockedShell.SessionsPanelId));
		Assert.Equal(2, raised);   // the layout's own change comes through
	}

	[Fact]
	public void TheMemory_TakesOverTheOldTreeSettingsOnce_AndIgnoresThemWhenNotPersistent()
	{
		var state = new MemoryClientState();
		state.State.TreeWidth = 450;
		var settings = new ClientSettingsStore(state, NullLogger<ClientSettingsStore>.Instance);
		settings.Update<ShellLayoutSettings>(s => s.TreeCollapsed = true);

		var start = new ShellLayoutMemory(settings, state, persistent: true).ReadStart();
		Assert.Null(start.Xml);
		Assert.Equal(450, start.LeftWidth);
		Assert.True(start.LeftAutoHidden);

		var test = new ShellLayoutMemory(settings, state, persistent: false);
		Assert.Equal(Constants.DockedShell.DefaultLeftPaneWidth, test.ReadStart().LeftWidth);
		Assert.False(test.ReadStart().LeftAutoHidden);
		test.Save("<x/>");
		Assert.Null(settings.Get<DockedShellLayoutSettings>().Layout);

		new ShellLayoutMemory(settings, state, persistent: true).Save("<x/>");
		var saved = new ShellLayoutMemory(settings, state, persistent: true).ReadStart();
		Assert.Equal("<x/>", saved.Xml);   // from now on the saved layout rules
		Assert.Equal(450, state.State.TreeWidth);   // the old values stay
	}
}
