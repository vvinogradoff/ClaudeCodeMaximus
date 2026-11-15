namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>schedules.remove</c>.</summary>
/// <param name="Removed">The schedule existed and is gone.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SchedulesRemoveResult(bool Removed);
