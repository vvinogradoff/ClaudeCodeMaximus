using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The tree the shell's tree commands act on. The tree is created when the daemon connection is ready, long after the commands are registered at startup,
/// so the commands reach it through this holder; the tree factory sets it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeContext
{
	/// <summary>The current tree; null until the connection is ready.</summary>
	public TreeViewModel? Current { get; set; }
}
