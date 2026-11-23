namespace TessynDesktop.Models.Remote;

/// <summary>The session that requested an elevated run (tdcp-spec 10.A13): its NodeId, name and short id.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ElevatedRequesterDto(string NodeId, string Name, string ShortId);
