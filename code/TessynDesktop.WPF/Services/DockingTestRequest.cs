namespace TessynDesktop.WPF.Services;

/// <summary>One request line of the docking test hook (FR.6.22): the operation, the panel, one argument (a side, a neighbour panel or on/off) and for float a rectangle.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record DockingTestRequest
{
	public string? Op { get; init; }

	public string? Panel { get; init; }

	/// <summary>dock: left, right, top or bottom; tab: the panel to stack on (or <c>session</c>); autohide: on or off.</summary>
	public string? Arg { get; init; }

	public double? X { get; init; }

	public double? Y { get; init; }

	public double? W { get; init; }

	public double? H { get; init; }
}
