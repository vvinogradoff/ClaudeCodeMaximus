namespace ClaudeMaximus.Models;

/// <summary>
/// A daemon profile representing a Claude account configuration.
/// Returned by profiles.list RPC.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynProfile
{
    public required string Name { get; init; }
    public string? ConfigDir { get; init; }
    public bool IsDefault { get; init; }
    public TessynProfileAuthInfo? Auth { get; init; }
}
