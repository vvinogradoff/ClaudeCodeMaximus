namespace TessynDesktop.WPF.ViewModels;

/// <summary>Whether a dragged session may be dropped on a row: the container it would go into, or the reason it is refused.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record DropDecision(bool Allowed, string? Reason, TreeNodeViewModel? Target)
{
	public static DropDecision Refuse(string reason) => new(false, reason, null);
}
