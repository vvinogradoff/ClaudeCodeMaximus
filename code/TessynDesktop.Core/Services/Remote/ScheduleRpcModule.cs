using System;
using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// <c>schedules.list</c>, <c>schedules.add</c> and <c>schedules.remove</c> (package J4, FR.14): the schedules of sessions as DTOs. Loopback only
/// (<c>remoteCallable: false</c>); the older <c>schedule.*</c> loopback methods keep their raw shapes. The logic is <see cref="IScheduleApiService"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScheduleRpcModule : IRemoteRpcModule
{
	private readonly IScheduleApiService _schedules;

	public ScheduleRpcModule(IScheduleApiService schedules)
	{
		_schedules = schedules;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync(RemoteProtocol.Schedules.ListMethod,   p => _schedules.List(TdcpParams.OptionalString(p, "nodeId")), remoteCallable: false);
		registry.AddSync(RemoteProtocol.Schedules.AddMethod,    Add,                                                           remoteCallable: false);
		registry.AddSync(RemoteProtocol.Schedules.RemoveMethod, p => _schedules.Remove(TdcpParams.RequireString(p, "scheduleId")), remoteCallable: false);
	}

	private object? Add(JsonNode? @params) =>
		_schedules.Add(new ScheduleAddRequestModel(
			TdcpParams.RequireString(@params, "nodeId"),
			TdcpParams.RequireString(@params, "prompt"),
			TdcpParams.RequireString(@params, "kind"),
			ReadFireAt(@params),
			TdcpParams.OptionalString(@params, "cron"),
			TdcpParams.OptionalString(@params, "missedFire"),
			TdcpParams.OptionalString(@params, "note"),
			TdcpParams.OptionalInt(@params, "maxFires")));

	private static DateTimeOffset? ReadFireAt(JsonNode? @params)
	{
		var text = TdcpParams.OptionalString(@params, "fireAtUtc");
		if (text == null)
			return null;

		return DateTimeOffset.TryParse(text, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var at)
			? at
			: throw new TdcpInvalidParamsException("'fireAtUtc' must be an ISO 8601 date and time");
	}
}
