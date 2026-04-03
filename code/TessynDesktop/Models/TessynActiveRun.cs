namespace TessynDesktop.Models;

/// <summary>An active run as returned by run.list / run.get.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynActiveRun
{
    public required string RunId { get; init; }
    public string? ExternalId { get; init; }
    public string? ProjectPath { get; init; }
    public string? Model { get; init; }
    public string State { get; init; } = "running";
}
