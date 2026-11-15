using System;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IScheduleApiService" />
/// <remarks>Created by Claude</remarks>
public sealed class ScheduleApiService : IScheduleApiService
{
	private const string FireOnce = "fireOnce";
	private const string Skip = "skip";

	private readonly ISchedulerService _scheduler;
	private readonly ISessionRegistry _registry;
	private readonly TimeProvider _time;

	public ScheduleApiService(ISchedulerService scheduler, ISessionRegistry registry, TimeProvider? time = null)
	{
		_scheduler = scheduler;
		_registry  = registry;
		_time      = time ?? TimeProvider.System;
	}

	/// <inheritdoc />
	public SchedulesListResult List(string? nodeId) =>
		new([.. _scheduler.GetSchedules(string.IsNullOrWhiteSpace(nodeId) ? null : nodeId).Select(s => s.ToDto())]);

	/// <inheritdoc />
	public ScheduleDto Add(ScheduleAddRequestModel request)
	{
		var prompt = request.Prompt ?? string.Empty;
		if (string.IsNullOrWhiteSpace(prompt))
			throw new TdcpInvalidParamsException(Constants.ScheduleApi.PromptRequired);
		if (prompt.Length > Constants.ScheduleApi.PromptMaxChars)
			throw new TdcpInvalidParamsException(Constants.ScheduleApi.PromptTooLong);

		var note = request.Note ?? string.Empty;
		if (note.Length > Constants.ScheduleApi.NoteMaxChars)
			throw new TdcpInvalidParamsException(Constants.ScheduleApi.NoteTooLong);

		var kind   = ParseKind(request.Kind);
		var missed = ParseMissedFire(request.MissedFire);

		if (kind == ScheduleKind.Cron)
		{
			if (request.FireAtUtc != null)
				throw new TdcpInvalidParamsException(Constants.ScheduleApi.FireAtNotAllowed);
			if (string.IsNullOrWhiteSpace(request.Cron))
				throw new TdcpInvalidParamsException(Constants.ScheduleApi.CronRequired);
			if (request.Cron.Length > Constants.ScheduleApi.CronMaxChars)
				throw new TdcpInvalidParamsException(Constants.ScheduleApi.CronInvalid);
		}
		else
		{
			if (!string.IsNullOrWhiteSpace(request.Cron))
				throw new TdcpInvalidParamsException(Constants.ScheduleApi.CronNotAllowed);

			CheckFireAt(request.FireAtUtc ?? throw new TdcpInvalidParamsException(Constants.ScheduleApi.FireAtRequired));
		}

		if (request.MaxFires is < 0 or > Constants.ScheduleApi.MaxFiresLimit)
			throw new TdcpInvalidParamsException(Constants.ScheduleApi.BadMaxFires);

		// The session is looked up last, so a malformed request is answered as one whether or not the session exists.
		var target = _registry.FindModelByNodeId(request.NodeId) ?? throw new TdcpException(Constants.ScheduleApi.NodeNotFound);

		var schedule = new ScheduleModel
		{
			ScheduleId       = Guid.NewGuid().ToString("N")[..8],
			TargetNodeId     = target.NodeId,
			Prompt           = prompt,
			Note             = note,
			Kind             = kind,
			FireAtUtc        = kind == ScheduleKind.Cron ? null : request.FireAtUtc,
			CronExpression   = kind == ScheduleKind.Cron ? request.Cron!.Trim() : null,
			MissedFirePolicy = missed,
			MaxFires         = kind == ScheduleKind.Cron ? request.MaxFires ?? Constants.Agent.MaxTurnsPerLoop : 0,
		};

		try
		{
			_scheduler.AddSchedule(schedule);
		}
		catch (ArgumentException)
		{
			throw new TdcpInvalidParamsException(Constants.ScheduleApi.CronInvalid);
		}

		return schedule.ToDto();
	}

	/// <inheritdoc />
	public SchedulesRemoveResult Remove(string scheduleId) => new(_scheduler.RemoveSchedule(scheduleId));

	private void CheckFireAt(DateTimeOffset fireAt)
	{
		var now = _time.GetUtcNow();
		if (fireAt < now.AddSeconds(-Constants.ScheduleApi.PastSlackSeconds))
			throw new TdcpInvalidParamsException(Constants.ScheduleApi.FireAtPast);
		if (fireAt > now.AddYears(Constants.ScheduleApi.MaxYearsAhead))
			throw new TdcpInvalidParamsException(Constants.ScheduleApi.FireAtTooFar);
	}

	private static ScheduleKind ParseKind(string? kind) => kind?.ToLowerInvariant() switch
	{
		RemoteProtocol.Schedules.Kinds.Delay => ScheduleKind.Delay,
		RemoteProtocol.Schedules.Kinds.At    => ScheduleKind.At,
		RemoteProtocol.Schedules.Kinds.Cron  => ScheduleKind.Cron,
		_ => throw new TdcpInvalidParamsException(Constants.ScheduleApi.UnknownKind),
	};

	private static MissedFirePolicy ParseMissedFire(string? value) => value switch
	{
		null or "" => MissedFirePolicy.FireOnce,
		FireOnce   => MissedFirePolicy.FireOnce,
		Skip       => MissedFirePolicy.Skip,
		_ => throw new TdcpInvalidParamsException(Constants.ScheduleApi.UnknownMissedFire),
	};
}
