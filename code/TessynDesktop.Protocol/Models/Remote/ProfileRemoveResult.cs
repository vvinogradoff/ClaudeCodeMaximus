namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>profile.remove</c>.</summary>
/// <param name="Profile">The removed profile.</param>
/// <param name="PinnedSessionCount">Sessions pinned by id to it. Their turns now fail with a "profile missing" error (ISSUE.013, never a silent Default) until they are switched; legacy sessions that only recorded a name run on Default with a note.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ProfileRemoveResult(ProfileDto Profile, int PinnedSessionCount);
