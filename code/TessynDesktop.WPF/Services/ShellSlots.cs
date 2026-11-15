namespace TessynDesktop.WPF.Services;

/// <summary><see cref="IShellSlots"/>: in-memory registry.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellSlots : IShellSlots
{
	private readonly object _gate = new();
	private readonly List<ShellSlotContribution> _all = [];

	public void Add(ShellSlotContribution contribution)
	{
		lock (_gate)
			_all.Add(contribution);
	}

	public IReadOnlyList<ShellSlotContribution> For(string slotId)
	{
		lock (_gate)
			return _all.Where(c => c.SlotId == slotId).OrderBy(c => c.Order).ToList();
	}
}
