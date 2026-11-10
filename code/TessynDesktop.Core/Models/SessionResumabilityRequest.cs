namespace TessynDesktop.Models;

/// <summary>One session's identity for a batch resumability check (see <c>IClaudeSessionStatusService.AreSessionsResumable</c>).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionResumabilityRequest(string WorkingDirectory, string? ClaudeSessionId);
