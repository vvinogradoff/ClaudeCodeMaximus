namespace TessynDesktop.Models.Agent;

/// <summary>Which sessions a <c>list_sessions</c> call covers (FR.15.13).</summary>
/// <remarks>Created by Claude</remarks>
public enum SessionListScope
{
	Project,
	All,
	Children,
	Subtree,
}
