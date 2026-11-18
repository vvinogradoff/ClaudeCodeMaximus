using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// A14 for the delayed send: the profile, model and effort the user picked when scheduling travel with the schedule (<c>schedules.add</c>, the stored
/// <see cref="ScheduleModel"/>, <see cref="ScheduleDto"/>) and the scheduler fires the turn with them through the same options an interactive send uses.
/// A schedule stored without them behaves exactly as before.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SchedulePicksTests : IDisposable
{
	private readonly ImportTestEnv _env = new();
	private readonly ManualTimeProvider _clock = new();
	private readonly FakeTurnService _turns = new();
	private readonly PicksResolver _resolver = new();
	private readonly SchedulerService _scheduler;
	private readonly ScheduleApiService _api;
	private readonly SessionNodeModel _session;

	public SchedulePicksTests()
	{
		var directory = _env.AddDirectory("proj");
		_session   = _env.Registry.AddSession(directory, "One");
		_scheduler = new SchedulerService(_env.Settings, _turns, _env.Registry, _resolver);
		_api       = new ScheduleApiService(_scheduler, _env.Registry, _clock);
	}

	public void Dispose() => _env.Dispose();

	/// <summary>Resolves the three params the way <see cref="TurnOptionsResolver"/> does (null when none is present) and remembers what it was asked.</summary>
	private sealed class PicksResolver : ITurnOptionsResolver
	{
		public JsonNode? LastParams { get; private set; }
		public bool RefuseProfile { get; set; }
		public bool FailWithIo { get; set; }

		public Task<TurnOptionsModel?> ResolveAsync(JsonNode? @params)
		{
			LastParams = @params?.DeepClone();
			var model = TdcpParams.OptionalString(@params, "modelId");
			var effort = TdcpParams.OptionalString(@params, "effort");
			var profile = TdcpParams.OptionalString(@params, "profileId");
			if (model == null && effort == null && profile == null)
				return Task.FromResult<TurnOptionsModel?>(null);

			if (FailWithIo)
				throw new System.IO.IOException("profile store unreadable");
			if (profile != null && (RefuseProfile || profile == "gone"))
				throw new TdcpInvalidParamsException($"Unknown profile '{profile}'");

			return Task.FromResult<TurnOptionsModel?>(new TurnOptionsModel(model, effort, profile == null ? null : "Name of " + profile));
		}
	}

	private ScheduleAddRequestModel Request(string? profile = null, string? model = null, string? effort = null) =>
		new(_session.NodeId, "wake", "at", _clock.GetUtcNow().AddMinutes(10), null, null, null, null, profile, model, effort);

	private async Task FireDueAsync(ScheduleModel schedule)
	{
		schedule.FireAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
		_scheduler.Poll();
		for (var i = 0; i < 200 && _turns.Calls.Count == 0; i++)
			await Task.Delay(25);

		Assert.Single(_turns.Calls);
	}

	[Fact]
	public void TheAddRequestPicks_AreStoredAndAnsweredInTheDto()
	{
		var dto = _api.Add(Request("work", "claude-opus-5-5", "high"));

		Assert.Equal("work", dto.ProfileId);
		Assert.Equal("claude-opus-5-5", dto.ModelId);
		Assert.Equal("high", dto.Effort);
		var stored = Assert.Single(_scheduler.GetSchedules());
		Assert.Equal(("work", "claude-opus-5-5", "high"), (stored.ProfileId, stored.ModelId, stored.Effort));
		Assert.Equal("work", Assert.Single(_api.List(null).Schedules).ProfileId);
	}

	[Fact]
	public void OnlyWhatWasPicked_IsStored_AndABlankPickIsNone()
	{
		var dto = _api.Add(Request(profile: "  ", model: "claude-opus-5-5"));

		Assert.Null(dto.ProfileId);
		Assert.Equal("claude-opus-5-5", dto.ModelId);
		Assert.Null(dto.Effort);
	}

	[Fact]
	public void ThePicks_SurviveASaveAndReload_AndAScheduleWithoutThemLoadsWithoutThem()
	{
		_api.Add(Request("work", "claude-opus-5-5", "high"));
		_api.Add(Request());

		var fresh = new AppSettingsService(_env.Root);
		fresh.Load();
		var reloaded = fresh.Settings.Schedules;

		Assert.Equal(2, reloaded.Count);
		Assert.Contains(reloaded, s => s is { ProfileId: "work", ModelId: "claude-opus-5-5", Effort: "high" });
		Assert.Contains(reloaded, s => s is { ProfileId: null, ModelId: null, Effort: null });
	}

	[Fact]
	public async Task AFire_RunsTheTurnWithTheStoredPicks_AndSendsOnlyWhatWasPicked()
	{
		_api.Add(Request(profile: "work", effort: "high"));

		await FireDueAsync(_scheduler.GetSchedules().Single());

		var options = Assert.Single(_turns.OptionsSeen);
		Assert.Equal(new TurnOptionsModel(null, "high", "Name of work"), options);
		var asked = Assert.IsType<JsonObject>(_resolver.LastParams);
		Assert.Equal(["effort", "profileId"], asked.Select(p => p.Key).Order());
	}

	[Fact]
	public async Task ALegacyScheduleWithoutPicks_FiresWithoutOptions_AsBefore()
	{
		_api.Add(Request());

		await FireDueAsync(_scheduler.GetSchedules().Single());

		Assert.Null(Assert.Single(_turns.OptionsSeen));
		Assert.Null(_resolver.LastParams);
	}

	[Fact]
	public async Task APickThatNoLongerResolves_StillRunsTheTurnOnTheSessionsOwnSettings()
	{
		_api.Add(Request(profile: "gone", model: "claude-opus-5-5"));

		await FireDueAsync(_scheduler.GetSchedules().Single());

		Assert.Null(Assert.Single(_turns.OptionsSeen));   // the message is not lost
	}

	[Fact]
	public async Task AnUnexpectedFailureWhileResolvingThePicks_StillRunsTheTurn()
	{
		_api.Add(Request(profile: "work"));
		_resolver.FailWithIo = true;

		await FireDueAsync(_scheduler.GetSchedules().Single());

		Assert.Null(Assert.Single(_turns.OptionsSeen));   // the one-shot schedule is already gone: the message must still run
	}

	[Fact]
	public async Task AMailboxDelivery_FiresWithoutOptions()
	{
		var supervisor = _env.Registry.AddSession(_env.Registry.Directories[0], "Boss");
		_scheduler.AddOrMergeMailboxDelivery(supervisor.NodeId, "worker", "done");

		await FireDueAsync(_scheduler.GetSchedules().Single());

		Assert.Null(Assert.Single(_turns.OptionsSeen));
	}

	[Fact]
	public async Task TheRpcModule_RefusesAnUnknownPick_BeforeStoringAnything_AndStoresAKnownOne()
	{
		var dispatcher = new TdcpDispatcher([new ScheduleRpcModule(_api, _resolver)]);

		async Task<JsonNode> CallAsync(string profile) => (await dispatcher.HandleTextAsync(new JsonObject
		{
			["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "schedules.add",
			["params"] = new JsonObject { ["nodeId"] = _session.NodeId, ["prompt"] = "p", ["kind"] = "at", ["fireAtUtc"] = _clock.GetUtcNow().AddMinutes(10).ToString("O"), ["profileId"] = profile },
		}.ToJsonString(), TdcpTransport.Loopback, CancellationToken.None))!;

		_resolver.RefuseProfile = true;
		var refused = await CallAsync("nope");
		Assert.Equal(-32602, refused["error"]!["code"]!.GetValue<int>());
		Assert.Empty(_scheduler.GetSchedules());

		_resolver.RefuseProfile = false;
		var accepted = await CallAsync("work");
		Assert.Equal("work", accepted["result"]!["profileId"]!.GetValue<string>());
	}

	// ── FR.12.35 — a schedule's picks are not sticky (TC.ME.02) ──────────────────

	[Fact]
	public async Task SchedulesAdd_PicksNotStored()
	{
		_api.Add(Request("work", "claude-opus-5-5", "high"));
		await FireDueAsync(_scheduler.GetSchedules().Single());

		Assert.Null(_session.ModelId);
		Assert.Null(_session.Effort);
	}
}
