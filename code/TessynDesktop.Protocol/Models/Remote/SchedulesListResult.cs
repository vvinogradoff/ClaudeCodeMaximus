using System;
using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>schedules.list</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SchedulesListResult(IReadOnlyList<ScheduleDto> Schedules);
