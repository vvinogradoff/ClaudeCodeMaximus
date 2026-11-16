using System;

namespace TessynDesktop.Models;

/// <summary>The parameters of <c>schedules.add</c>, read but not yet judged.</summary>
/// <param name="NodeId">The session to wake.</param>
/// <param name="Prompt">The prompt to send.</param>
/// <param name="Kind">"delay", "at" or "cron".</param>
/// <param name="FireAtUtc">The fire time of a delay or at schedule.</param>
/// <param name="Cron">The cron expression of a cron schedule.</param>
/// <param name="MissedFire">"fireOnce" (default) or "skip".</param>
/// <param name="Note">A human note.</param>
/// <param name="MaxFires">The most fires of a cron schedule; omitted = the default of the agent tools.</param>
/// <param name="ProfileId">Optional: the profile picked at scheduling time (<c>"default"</c> or a profile id); the turn runs on it when the schedule fires (A14).</param>
/// <param name="ModelId">Optional: the model picked at scheduling time.</param>
/// <param name="Effort">Optional: the effort picked at scheduling time.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ScheduleAddRequestModel(string NodeId, string Prompt, string Kind, DateTimeOffset? FireAtUtc, string? Cron, string? MissedFire, string? Note, int? MaxFires, string? ProfileId = null, string? ModelId = null, string? Effort = null);
