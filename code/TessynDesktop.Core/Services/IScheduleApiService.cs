using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>The schedules of sessions (FR.14) as DTOs: list, validated add and remove for the remote <c>schedules.*</c> methods.</summary>
/// <remarks>Created by Claude</remarks>
public interface IScheduleApiService
{
	/// <summary>The schedules of <paramref name="nodeId"/>, or of every session when it is null.</summary>
	SchedulesListResult List(string? nodeId);

	/// <summary>Validates the request and adds the schedule; an invalid request is a <see cref="TdcpInvalidParamsException"/> naming the field, an unknown session a <see cref="TdcpException"/>.</summary>
	ScheduleDto Add(ScheduleAddRequestModel request);

	/// <summary>Removes a schedule; false when there is none with that id.</summary>
	SchedulesRemoveResult Remove(string scheduleId);
}
