namespace TessynDesktop.Models;

/// <summary>The result of <c>request_elevated_run</c>: the run, and whether it is new (false for a dedupe hit on an equal pending request).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ElevatedRunRequestOutcomeModel(ElevatedRunModel Run, bool IsCreated);
