namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>elevated.get</c>: the run and, with <c>tailLines</c> above 0, the last lines of its <c>output.log</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ElevatedGetResult(ElevatedRunDto Run, string? OutputTail = null);
