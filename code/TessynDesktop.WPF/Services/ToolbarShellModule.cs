using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Services;

/// <summary>Puts the instruction toolbar (W4) into the title bar and the toggles with the local-tools button into the command bar, after the selectors.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ToolbarShellModule : IShellModule
{
	/// <summary>After the selectors of the turn loop (order 10).</summary>
	private const int TogglesOrder = 20;

	private const int ToolbarOrder = 10;

	public void Register(IShellSlots slots, IShellCommands commands)
	{
		slots.Add(new ShellSlotContribution { SlotId = Constants.Slots.TitleBarToolbar, Order = ToolbarOrder, Create = () => new InstructionToolbar() });
		slots.Add(new ShellSlotContribution { SlotId = Constants.Slots.CommandBar, Order = TogglesOrder, Create = () => new CommandToggles() });
	}
}
