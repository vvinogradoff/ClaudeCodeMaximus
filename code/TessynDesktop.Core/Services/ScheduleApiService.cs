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

			CheckCronInterval(request.Cron);
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
		CheckCapacity(target.NodeId);

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
			ProfileId        = NullIfBlank(request.ProfileId),
			ModelId          = NullIfBlank(request.ModelId),
			Effort           = NullIfBlank(request.Effort),
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

	/// <summary>A runaway client must not fill the settings file: schedules are persisted with their prompt.</summary>
	private void CheckCapacity(string nodeId)
	{
		var all = _scheduler.GetSchedules().Where(s => !s.IsMailboxDelivery).ToList();
		if (all.Count >= Constants.ScheduleApi.MaxTotal)
			throw new TdcpInvalidParamsException(Constants.ScheduleApi.TooManyInTotal);
		if (all.Count(s => s.TargetNodeId == nodeId) >= Constants.ScheduleApi.MaxPerSession)
			throw new TdcpInvalidParamsException(Constants.ScheduleApi.TooManyForSession);
	}

	/// <summary>The first fire gaps of the expression must all be at least the minimum; an invalid expression is refused here too.</summary>
	private void CheckCronInterval(string cron)
	{
		try
		{
			var at = _time.GetUtcNow();
			for (var i = 0; i <= Constants.ScheduleApi.CronGapsChecked; i++)
			{
				var next = CronHelper.TryGetNextOccurrence(cron, at);
				if (next == null)
					return;   // valid but never matches: parked, as the scheduler does
				if (i > 0 && next.Value - at < TimeSpan.FromMinutes(Constants.ScheduleApi.MinCronIntervalMinutes))
					throw new TdcpInvalidParamsException(Constants.ScheduleApi.CronTooFrequent);
				at = next.Value;
			}
		}
		catch (ArgumentException)
		{
			throw new TdcpInvalidParamsException(Constants.ScheduleApi.CronInvalid);
		}
	}

	private void CheckFireAt(DateTimeOffset fireAt)
	{
		var now = _time.GetUtcNow();
		if (fireAt < now.AddSeconds(-Constants.ScheduleApi.PastSlackSeconds))
			throw new TdcpInvalidParamsException(Constants.ScheduleApi.FireAtPast);
		if (fireAt > now.AddYears(Constants.ScheduleApi.MaxYearsAhead))
			throw new TdcpInvalidParamsException(Constants.ScheduleApi.FireAtTooFar);
	}

	private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

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
