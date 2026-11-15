using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Services;

/// <summary>W1b: puts the composer notices (sign-in needed, code index building) into the accessory slot above the prompt box.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ComposerShellModule : IShellModule
{
	private const int NoticesOrder = 10;

	public void Register(IShellSlots slots, IShellCommands commands)
	{
		slots.Add(new ShellSlotContribution { SlotId = Constants.Slots.ComposerAccessory, Order = NoticesOrder, Create = () => new ComposerNotices() });
	}
}
