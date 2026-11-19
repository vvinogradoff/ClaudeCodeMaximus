namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>One reply of the docking test hook: whether the operation worked, the reason when not, and every panel's state afterwards.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed record DockingHookReply(bool Ok, string? Error, IReadOnlyList<DockingHookPanel> Panels)
{
	/// <summary>The state of one panel; fails the test when the reply does not list it.</summary>
	public DockingHookPanel Panel(string id) => Panels.FirstOrDefault(p => p.Id == id) ?? throw new Xunit.Sdk.XunitException($"The hook state lists no panel '{id}': [{string.Join(", ", Panels.Select(p => p.Id))}]");

	public bool HasPanel(string id) => Panels.Any(p => p.Id == id);
}
