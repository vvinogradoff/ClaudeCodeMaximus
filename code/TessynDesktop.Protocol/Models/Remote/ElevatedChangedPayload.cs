namespace TessynDesktop.Models.Remote;

/// <summary>Event <c>elevated.changed</c> (tdcp-spec 10.A13): a run changed status. Local stream only.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ElevatedChangedPayload(string RequestId, string NodeId, ElevatedRunStatus Status);
