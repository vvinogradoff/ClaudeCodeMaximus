namespace TessynDesktop.WPF.Services;

/// <summary>One reply line of the docking test hook: whether it worked, the reason when not, and the state of every panel after the operation.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record DockingTestReply
{
	public bool Ok { get; init; }

	public string? Error { get; init; }

	public IReadOnlyList<DockingPanelState> Panels { get; init; } = [];
}
