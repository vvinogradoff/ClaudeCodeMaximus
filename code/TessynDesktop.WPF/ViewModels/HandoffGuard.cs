namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// Counts the hand-offs (finished turn -> persisted entry) in flight per session. While one is, the session's live row is not pushed (R1). A count,
/// not a flag: with two overlapping hand-offs of one session the first to end must not lift the guard of the other (L1).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class HandoffGuard
{
	private readonly Dictionary<string, int> _counts = new();

	public void Enter(string nodeId) => _counts[nodeId] = _counts.GetValueOrDefault(nodeId) + 1;

	public void Exit(string nodeId)
	{
		var left = _counts.GetValueOrDefault(nodeId) - 1;
		if (left > 0)
			_counts[nodeId] = left;
		else
			_counts.Remove(nodeId);
	}

	public bool IsActive(string nodeId) => _counts.ContainsKey(nodeId);
}
