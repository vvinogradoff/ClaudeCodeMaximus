using System;
using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>A schedule as clients see it (<c>schedules.list</c>, <c>schedules.add</c>).</summary>
/// <param name="Id">The schedule id.</param>
/// <param name="NodeId">The session the schedule wakes.</param>
/// <param name="Kind">Delay, at or cron.</param>
/// <param name="Prompt">The prompt sent when the schedule fires.</param>
/// <param name="Note">A human note.</param>
/// <param name="FireAtUtc">The next fire time (delay, at and cron); null for a cron schedule that never matches.</param>
/// <param name="Cron">The cron expression of a cron schedule.</param>
/// <param name="MissedFire">The policy for fires missed while the daemon was down.</param>
/// <param name="FireCount">How often the schedule has fired.</param>
/// <param name="MaxFires">The most fires of a cron schedule before it cancels itself; 0 = no limit.</param>
/// <param name="IsMailbox">A Mailbox Delivery of an async worker (FR.15), not a user schedule.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ScheduleDto(string Id, string NodeId, ScheduleKindDto Kind, string Prompt, string Note, DateTimeOffset? FireAtUtc, string? Cron, MissedFireDto MissedFire, int FireCount, int MaxFires, bool IsMailbox);
