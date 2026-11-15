using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Services;

/// <summary>Puts the selectors of the turn loop (W1a) into the command-bar slot of the session pane.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnLoopShellModule : IShellModule
{
	/// <summary>Order of the selectors in the command bar; later packages (toggles, local tools) contribute after them.</summary>
	private const int SelectorsOrder = 10;

	public void Register(IShellSlots slots, IShellCommands commands)
	{
		slots.Add(new ShellSlotContribution { SlotId = Constants.Slots.CommandBar, Order = SelectorsOrder, Create = () => new CommandBar() });
	}
}
