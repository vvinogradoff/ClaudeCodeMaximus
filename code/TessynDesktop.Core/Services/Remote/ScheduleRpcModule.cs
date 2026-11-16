using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
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
	private readonly ITurnOptionsResolver? _options;

	/// <param name="schedules">The schedule logic.</param>
	/// <param name="options">Validates the optional <c>profileId</c>, <c>modelId</c> and <c>effort</c> of <c>schedules.add</c> exactly as <c>session.send</c> does; without it they are stored unchecked (tests).</param>
	public ScheduleRpcModule(IScheduleApiService schedules, ITurnOptionsResolver? options = null)
	{
		_schedules = schedules;
		_options   = options;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync(RemoteProtocol.Schedules.ListMethod,   p => _schedules.List(TdcpParams.OptionalString(p, "nodeId")), remoteCallable: false);
		registry.Add    (RemoteProtocol.Schedules.AddMethod,    async (p, _) => await AddAsync(p),                              remoteCallable: false);
		registry.AddSync(RemoteProtocol.Schedules.RemoveMethod, p => _schedules.Remove(TdcpParams.RequireString(p, "scheduleId")), remoteCallable: false);
	}

	private async Task<object?> AddAsync(JsonNode? @params)
	{
		// A14: an unknown model, effort or profile is refused now, not when the schedule fires hours later.
		if (_options != null)
			await _options.ResolveAsync(@params);

		return _schedules.Add(new ScheduleAddRequestModel(
			TdcpParams.RequireString(@params, "nodeId"),
			TdcpParams.RequireString(@params, "prompt"),
			TdcpParams.RequireString(@params, "kind"),
			ReadFireAt(@params),
			TdcpParams.OptionalString(@params, "cron"),
			TdcpParams.OptionalString(@params, "missedFire"),
			TdcpParams.OptionalString(@params, "note"),
			TdcpParams.OptionalInt(@params, "maxFires"),
			TdcpParams.OptionalString(@params, "profileId"),
			TdcpParams.OptionalString(@params, "modelId"),
			TdcpParams.OptionalString(@params, "effort")));
	}

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
