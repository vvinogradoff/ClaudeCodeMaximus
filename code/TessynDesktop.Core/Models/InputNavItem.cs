namespace TessynDesktop.Models;

/// <summary>One transcript entry as input navigation sees it (FR.2.6): whether it is an input (a USER entry) and whether an automated one.</summary>
/// <remarks>Created by Claude</remarks>
public readonly record struct InputNavItem(bool IsInput, bool IsAutomated);
