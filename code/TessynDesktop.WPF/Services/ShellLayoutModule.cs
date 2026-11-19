namespace TessynDesktop.WPF.Services;

/// <summary>
/// The Panels group of the hamburger menu (FR.6.14, FR.6.19): one entry per closable panel (Sessions, Recent, Tags, Focus) with a check mark while it is open, which shows a closed
/// panel at its last place or brings an open one forward, and <b>Reset layout…</b>, which asks first and then restores the default layout. The group sits after the registered
/// entries and before Exit.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellLayoutModule : IShellModule
{
	private readonly IShellLayout _layout;
	private readonly IDialogService _dialogs;

	public ShellLayoutModule(IShellLayout layout, IDialogService dialogs)
	{
		_layout = layout;
		_dialogs = dialogs;
	}

	public void Register(IShellSlots slots, IShellCommands commands)
	{
		var order = Constants.ShellLayout.PanelsMenuOrder;
		AddPanel(commands, Constants.ShellLayout.ShowSessionsCommandId, Constants.ShellLayout.SessionsTitle, Constants.DockedShell.SessionsPanelId, order++);
		AddPanel(commands, Constants.ShellLayout.ShowRecentCommandId, Constants.SessionLists.RecentTabTitle, Constants.DockedShell.RecentPanelId, order++);
		AddPanel(commands, Constants.ShellLayout.ShowTagsCommandId, Constants.SessionLists.TagsTabTitle, Constants.DockedShell.TagsPanelId, order++);
		AddPanel(commands, Constants.ShellLayout.ShowFocusCommandId, Constants.ShellLayout.FocusTitle, Constants.DockedShell.FocusPanelId, order++);
		commands.Register(new ShellCommand
		{
			Id = Constants.ShellLayout.ResetCommandId,
			Title = Constants.ShellLayout.ResetCommandTitle,
			AllowInTextInput = true,
			ExecuteAsync = ResetAsync,
		});
		commands.AddMenuEntry(Constants.Menus.Main, Constants.ShellLayout.ResetCommandId, order, Constants.ShellLayout.PanelsMenuGroup);
	}

	private void AddPanel(IShellCommands commands, string commandId, string title, string panelId, int order)
	{
		commands.Register(new ShellCommand
		{
			Id = commandId,
			Title = title,
			AllowInTextInput = true,
			IsChecked = _ => _layout.IsOpen(panelId),
			ExecuteAsync = _ =>
			{
				_layout.BringForward(panelId);
				return Task.CompletedTask;
			},
		});
		commands.AddMenuEntry(Constants.Menus.Main, commandId, order, Constants.ShellLayout.PanelsMenuGroup);
	}

	private async Task ResetAsync(object? parameter)
	{
		var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(Constants.ShellLayout.ResetDialogTitle, Constants.ShellLayout.ResetDialogMessage, Constants.ShellLayout.ResetDialogConfirm));
		if (confirmed)
			_layout.ResetLayout();
	}
}
