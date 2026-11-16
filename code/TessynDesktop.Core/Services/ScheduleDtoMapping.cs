using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>Schedule model to wire DTO. The raw model never leaves the daemon.</summary>
/// <remarks>Created by Claude</remarks>
public static class ScheduleDtoMapping
{
	public static ScheduleDto ToDto(this ScheduleModel model) => new(
		model.ScheduleId,
		model.TargetNodeId,
		model.Kind switch { ScheduleKind.At => ScheduleKindDto.At, ScheduleKind.Cron => ScheduleKindDto.Cron, _ => ScheduleKindDto.Delay },
		model.Prompt,
		model.Note,
		model.FireAtUtc,
		model.CronExpression,
		model.MissedFirePolicy == MissedFirePolicy.Skip ? MissedFireDto.Skip : MissedFireDto.FireOnce,
		model.FireCount,
		model.MaxFires,
		model.IsMailboxDelivery,
		model.ProfileId,
		model.ModelId,
		model.Effort);
}
