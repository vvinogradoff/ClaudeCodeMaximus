namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>session.appended</c> and <c>session.reset</c>: the session and its current history epoch.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionEpochPayload(string NodeId, int Epoch);
