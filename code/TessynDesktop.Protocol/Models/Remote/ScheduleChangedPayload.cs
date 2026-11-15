namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>schedule.changed</c> (package J4, local stream only): a schedule was added, removed or fired.</summary>
/// <param name="NodeId">The session whose schedules changed; null when more than one session may be affected.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ScheduleChangedPayload(string? NodeId);
