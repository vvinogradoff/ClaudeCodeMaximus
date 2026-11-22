namespace TessynDesktop.Models;

/// <summary>A ChatGPT profile that was deleted (FR.33.5): the profile as it was listed, and how many sessions are still pinned to it (their turns now fail with the missing-pin error).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexProfileRemovalModel(CodexProfileInfoModel Profile, int PinnedSessionCount);
