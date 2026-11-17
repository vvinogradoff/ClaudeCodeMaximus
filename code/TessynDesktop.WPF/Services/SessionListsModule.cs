using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// W3: puts the Recent and Tags tabs into the left panel, the Recent dropdown into the toolbar row, the favourite star into the session
/// header, and the favourite command into the tree's context menu. No existing view is edited.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionListsModule : IShellModule
{
	private readonly SessionListsHost _host;

	public SessionListsModule(SessionListsHost host)
	{
		_host = host;
	}

	public void Register(IShellSlots slots, IShellCommands commands)
	{
		slots.Add(new ShellSlotContribution
		{
			SlotId = Constants.Slots.LeftPanelTabs,
			Order = Constants.SessionLists.RecentTabOrder,
			Title = Constants.SessionLists.RecentTabTitle,
			Create = () => new RecentPanel { DataContext = _host.RecentTab },
		});
		slots.Add(new ShellSlotContribution
		{
			SlotId = Constants.Slots.LeftPanelTabs,
			Order = Constants.SessionLists.TagsTabOrder,
			Title = Constants.SessionLists.TagsTabTitle,
			Create = () => new TagsPanel { DataContext = _host.Tags },
		});
		slots.Add(new ShellSlotContribution
		{
			SlotId = Constants.Slots.TitleBarCenter,
			Order = Constants.SessionLists.RecentDropdownOrder,
			Create = () => new RecentDropdown { DataContext = _host.Recent },
		});
		slots.Add(new ShellSlotContribution
		{
			SlotId = Constants.Slots.SessionHeaderToolbar,
			Order = Constants.SessionLists.FavoriteButtonOrder,
			Create = () => new FavoriteButton(),
		});

		commands.Register(new ShellCommand
		{
			Id = Constants.SessionLists.ToggleFavoriteCommandId,
			Title = Constants.SessionLists.ToggleFavoriteTitle,
			CanExecute = parameter => parameter is TreeNodeViewModel { IsSessionLike: true, State: not null },
			ExecuteAsync = ToggleFavoriteAsync,
		});
		commands.AddMenuEntry(Constants.Menus.TreeNode, Constants.SessionLists.ToggleFavoriteCommandId, Constants.SessionLists.FavoriteMenuOrder, Constants.SessionLists.FavoriteMenuGroup);
	}

	private async Task ToggleFavoriteAsync(object? parameter)
	{
		if (parameter is not TreeNodeViewModel { State: { } state } node)
			return;

		var value = await _host.SetFavoriteAsync(node.Id, !state.Favorite);
		if (value != null)
			node.State = state with { Favorite = value.Value };
	}
}
