namespace ClaudeMaximus.Models;

/// <summary>
/// Auth status for a single Tessyn daemon profile.
/// Returned as part of profiles.list response.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynProfileAuthInfo
{
    public bool LoggedIn { get; init; }
    public string? Email { get; init; }
    public string? AuthMethod { get; init; }
    public string? SubscriptionType { get; init; }
}
