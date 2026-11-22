using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Contract;

namespace TessynDesktop.Tests.Client;

/// <summary>
/// The FR.33 surface of <see cref="TessynClient"/> (tdcp-spec 10.A14) against a real loopback daemon with the Codex fake: the engines of the hello,
/// the typed catalog, usage, profile list and settings members, the ChatGPT sign-in, cancel and sign-out wrappers, Reset to defaults through the
/// settings patch, and the typed <c>profiles.codexLoginCompleted</c> notification.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class EnginesClientTests : IAsyncLifetime
{
	private readonly CodexWireEnv _env = new();

	public Task InitializeAsync() => Task.CompletedTask;

	public async Task DisposeAsync() => await _env.DisposeAsync();

	private TessynClient NewClient() =>
		new(new Uri(_env.Host.BaseUrl + Constants.Agent.TdcpEndpointPath), TdcpTestHost.Token);

	[Fact]
	public async Task Hello_catalog_usage_and_the_profile_list_read_as_typed_engine_members()
	{
		await _env.Codex.LoadCatalogAsync("codex_1");
		await using var client = NewClient();
		await client.ConnectAsync();

		var catalog  = await client.CatalogGetAsync();
		var usage    = await client.UsageGetAsync("codex_1");
		var profiles = await client.ProfileListAsync();

		Assert.Equal(["claude", "codex"], client.ServerInfo!.Engines);
		Assert.Contains(RemoteProtocol.CodexProfilesAdmin.Capability, client.ServerInfo.Capabilities);
		var sol = catalog.Models.Single(m => m.Id == "gpt-x-sol");
		Assert.Equal((RemoteProtocol.Engines.Codex, ModelProvider.OpenAI, true), (sol.Engine, sol.Provider, sol.IsDefault));
		Assert.Equal(["low", "medium", "high", "xhigh", "max"], sol.Efforts!.Select(e => e.Id));
		Assert.Contains(catalog.Profiles, p => p.Id == "codex_1" && p.Engine == RemoteProtocol.Engines.Codex && p.Plan == "plus");
		Assert.Equal((RemoteProtocol.Engines.Codex, "5h", 300), (usage.Engine, usage.FiveHour!.Label, usage.FiveHour.DurationMinutes));
		Assert.Contains(profiles.CodexProfiles!, p => p.Id == "codex_1" && p.SignedIn);
	}

	[Fact]
	public async Task Sign_in_completes_through_the_typed_notification_and_sign_out_answers_the_profile()
	{
		await using var client = NewClient();
		var probe = new ClientProbe(client);
		await client.ConnectAsync();

		var started = await client.ProfileCodexLoginAsync(RemoteProtocol.CodexProfilesAdmin.BrowserMode);
		await _env.CompleteLoginAsync(started.ProfileId, started.LoginId, success: true);
		var completed = await NextAsync(probe, n => n.TryGetCodexLoginCompleted(out _));
		var signedOut = await client.ProfileCodexLogoutAsync(started.ProfileId);

		Assert.Equal(("codex_1", "https://auth.openai.com/oauth/authorize?fake=1"), (started.ProfileId, started.AuthUrl));
		Assert.True(completed.TryGetCodexLoginCompleted(out var payload));
		Assert.Equal(new ProfileCodexLoginCompletedPayload(started.LoginId, "codex_1", true, null, "u@example.com", "plus"), payload);
		Assert.Equal(("codex_1", false), (signedOut.Id, signedOut.SignedIn));
	}

	[Fact]
	public async Task A_device_code_sign_in_can_be_cancelled()
	{
		await using var client = NewClient();
		var probe = new ClientProbe(client);
		await client.ConnectAsync();

		var started   = await client.ProfileCodexLoginAsync(RemoteProtocol.CodexProfilesAdmin.DeviceCodeMode);
		var cancelled = await client.ProfileCodexLoginCancelAsync(started.LoginId);
		var completed = await NextAsync(probe, n => n.TryGetCodexLoginCompleted(out _));

		Assert.Equal("ABCD-1234", started.UserCode);
		Assert.True(cancelled.Cancelled);
		Assert.True(completed.TryGetCodexLoginCompleted(out var payload));
		Assert.False(payload.Success);
	}

	[Fact]
	public async Task The_general_section_reads_the_Codex_members_and_Reset_to_defaults_goes_through_the_patch()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var before = await client.SettingsGetAsync<GeneralSettingsDto>(RemoteProtocol.SettingsApi.GeneralSection);
		var edited = await client.SettingsUpdateAsync(RemoteProtocol.SettingsApi.GeneralSection,
			new GeneralSettingsPatch(CodexModelTiers: [new CodexModelTierDto("sol", 3)]), before.Version);
		var reset = await client.SettingsUpdateAsync(RemoteProtocol.SettingsApi.GeneralSection, new GeneralSettingsPatch(ResetCodexModelTiers: true), edited.Version);
		var after = await client.SettingsGetAsync<GeneralSettingsDto>(RemoteProtocol.SettingsApi.GeneralSection);

		Assert.Equal((true, "0.171.0", CodexWireEnv.CodexExe), (before.Values.CodexStatus!.Found, before.Values.CodexStatus.Version, before.Values.CodexPath));
		Assert.True(before.Values.CodexModelTiersAreDefaults);
		Assert.True(reset.Version > edited.Version);
		Assert.True(after.Values.CodexModelTiersAreDefaults);
		Assert.Equal(5, after.Values.CodexModelTiers!.Count);
	}

	private static async Task<RemoteNotification> NextAsync(ClientProbe probe, Func<RemoteNotification, bool> match)
	{
		while (true)
		{
			var notification = await probe.NextNotificationAsync();
			if (match(notification))
				return notification;
		}
	}
}
