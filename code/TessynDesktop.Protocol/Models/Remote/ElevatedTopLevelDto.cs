namespace TessynDesktop.Models.Remote;

/// <summary>The top-level supervisor of a worker that requested an elevated run (tdcp-spec 10.A13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ElevatedTopLevelDto(string NodeId, string Name);
