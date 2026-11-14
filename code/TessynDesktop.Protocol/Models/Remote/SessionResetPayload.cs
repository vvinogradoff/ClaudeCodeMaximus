namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>session.reset</c>: the session, its history epoch and (P1.F) the new content generation.</summary>
/// <param name="NodeId">The session's NodeId.</param>
/// <param name="Epoch">Transcript epoch after the rewrite.</param>
/// <param name="FileId">New content generation; omitted without a content store.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionResetPayload(string NodeId, int Epoch, string? FileId = null);
