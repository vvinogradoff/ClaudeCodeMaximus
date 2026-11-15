using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// W5: puts the profile flyout in the status bar, the auth-error banner above the prompt box, a "Switch profile…" button under the tree rows, and the
/// switch command (ISSUE.013 R1) in the tree's context menu. No existing view is edited by this module.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileShellModule : IShellModule
{
	private readonly ProfileHost _host;

	public ProfileShellModule(ProfileHost host)
	{
		_host = host;
	}

	public void Register(IShellSlots slots, IShellCommands commands)
	{
		slots.Add(new ShellSlotContribution
		{
			SlotId = Constants.Slots.StatusBarItems,
			Order = Constants.Profiles.FlyoutOrder,
			Create = () => new ProfileFlyout { DataContext = _host.Flyout },
		});
		slots.Add(new ShellSlotContribution
		{
			SlotId = Constants.Slots.ComposerAccessory,
			Order = Constants.Profiles.AuthBannerOrder,
			Create = () => new AuthBanner { DataContext = _host.Banner },
		});
		slots.Add(new ShellSlotContribution
		{
			SlotId = Constants.Slots.TreeFooter,
			Order = Constants.Profiles.SwitchButtonOrder,
			Create = () => new ProfileSwitchButton(commands),
		});

		commands.Register(new ShellCommand
		{
			Id = Constants.Profiles.SwitchCommandId,
			Title = Constants.Profiles.SwitchCommandTitle,
			CanExecute = node => ProfileSwitchViewModel.CanSwitch(node as TreeNodeViewModel),
			ExecuteAsync = node => _host.Switcher.SwitchAsync(node as TreeNodeViewModel),
		});
		commands.AddMenuEntry(Constants.Menus.TreeNode, Constants.Profiles.SwitchCommandId, Constants.Profiles.SwitchMenuOrder, Constants.Profiles.SwitchMenuGroup);
	}
}
