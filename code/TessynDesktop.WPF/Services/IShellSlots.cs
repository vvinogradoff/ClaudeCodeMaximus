namespace TessynDesktop.WPF.Services;

/// <summary>
/// Registry of the views that fill the shell's empty layout slots. <c>MainWindow</c>, <c>SessionPane</c> and <c>TreePanel</c> host the
/// slots (<c>SlotHost</c>, and the docking area's panel catalog for the left panels); a package registers its UserControl here instead of editing those files.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IShellSlots
{
	void Add(ShellSlotContribution contribution);

	/// <summary>The contributions of a slot in display order.</summary>
	IReadOnlyList<ShellSlotContribution> For(string slotId);
}
