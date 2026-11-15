namespace TessynDesktop.WPF.Services;

/// <summary>A registered menu entry: which command sits where in which menu.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellMenuEntryModel
{
	public required string MenuId { get; init; }

	public required string CommandId { get; init; }

	public int Order { get; init; }

	public string? Group { get; init; }

	/// <summary>Registration sequence number, the tie-breaker of equal orders.</summary>
	public int Sequence { get; init; }
}
