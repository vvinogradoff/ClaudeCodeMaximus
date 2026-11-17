using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>The package J3 part 2 members of <see cref="FakeTessynClient"/>: scriptable, recording, and usable through <see cref="ITessynClient"/> as the WPF packages will.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeTessynClientIntegrationsTests
{
	[Fact]
	public async Task TheMcpRegistry_CanBeFilledEditedEnabledAndEmptied_AndASecretIsNeverAnswered()
	{
		var fake = new FakeTessynClient();
		ITessynClient client = fake;

		var created = await client.McpUpsertAsync(new McpServerUpsertRequest(DisplayName: "Docs", Url: "https://docs.example.com/mcp", AuthType: McpAuthTypeDto.StaticHeader, StaticHeaderName: "Authorization", StaticHeaderValue: "secret-header-0001"));
		var renamed = await client.McpUpsertAsync(new McpServerUpsertRequest(Id: created.Id, DisplayName: "Docs 2"));
		var disabled = await client.McpSetEnabledAsync(created.Id, false);
		var list = await client.McpListAsync();
		var removed = await client.McpRemoveAsync(created.Id);

		Assert.Equal(("Docs", true), (created.DisplayName, created.HasStaticHeaderValue));
		Assert.Equal(("Docs 2", true, "https://docs.example.com/mcp"), (renamed.DisplayName, renamed.HasStaticHeaderValue, renamed.Url));
		Assert.False(disabled.Enabled);
		Assert.Equal([created.Id], list.Servers.Select(s => s.Id));
		Assert.True(removed.Removed);
		Assert.Empty((await client.McpListAsync()).Servers);
		Assert.DoesNotContain("secret-header-0001", System.Text.Json.JsonSerializer.Serialize(list));
		Assert.Equal(2, fake.McpUpserts.Count);
		Assert.Equal(["mcp.upsert:new", $"mcp.upsert:{created.Id}", $"mcp.setEnabled:{created.Id}:False", "mcp.list", $"mcp.remove:{created.Id}", "mcp.list"], fake.Calls);
	}

	[Fact]
	public async Task McpDiscoverAndAuthorize_AnswerWhatTheTestScripted()
	{
		var fake = new FakeTessynClient
		{
			McpDiscoverAnswer = new McpDiscoverResult(new McpDiscoveredConfigDto("https://a/authorize", "https://a/token", null, true), TimedOut: true),
			McpAuthorizeAnswer = new McpAuthorizeResultDto(false, "Timed out waiting for the browser authorization redirect."),
		};

		var discovered = await fake.McpDiscoverAsync("https://tracker.example.com/mcp");
		var authorized = await fake.McpAuthorizeAsync("mcp1");

		Assert.True(discovered.TimedOut);
		Assert.Equal("https://a/token", discovered.Config.TokenEndpoint);
		Assert.False(authorized.Authorized);
		Assert.Equal(["mcp.discover:https://tracker.example.com/mcp", "mcp.authorize:mcp1"], fake.Calls);
	}

	[Fact]
	public async Task AMcpFailure_Surfaces_AndAnUnknownServerIsInvalidParams()
	{
		var fake = new FakeTessynClient();

		var unknown = await Assert.ThrowsAsync<TessynRpcException>(() => fake.McpSetEnabledAsync("nope", true));
		fake.McpException = new TessynRpcException(RemoteProtocol.ErrorCodes.DaemonError, "broken");

		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, unknown.Code);
		await Assert.ThrowsAsync<TessynRpcException>(() => fake.McpListAsync());
	}

	[Fact]
	public async Task Telegram_FollowsTheToken_AndTheEnabledFlag()
	{
		var fake = new FakeTessynClient { TelegramStatus = new TelegramStatusDto(Enabled: true, Running: false, HasToken: false) };

		var set = await fake.TelegramSetTokenAsync("any-token");
		var cleared = await fake.TelegramClearTokenAsync();

		Assert.Equal(new TelegramStatusDto(true, true, true), set);
		Assert.Equal(new TelegramStatusDto(true, false, false), cleared);
		Assert.Equal(["telegram.setToken", "telegram.clearToken"], fake.Calls);
	}

	[Fact]
	public async Task Relay_AppliesTheRequestToItsStatus_AndKeepsTheTokenWriteOnly()
	{
		var fake = new FakeTessynClient();

		var on = await fake.RelayConfigureAsync(new RelayConfigureRequest(Enabled: true, Url: "https://web.example.com", LinkToken: "link-token-0001"));
		var off = await fake.RelayConfigureAsync(new RelayConfigureRequest(Enabled: false));

		Assert.Equal((true, RelayStateDto.Connecting, "https://web.example.com", true), (on.Enabled, on.State, on.Url, on.HasToken));
		Assert.Equal((false, RelayStateDto.Disabled, "https://web.example.com", true), (off.Enabled, off.State, off.Url, off.HasToken));
		Assert.Equal(["link-token-0001"], fake.RelayTokensReceived);
		Assert.DoesNotContain("link-token-0001", System.Text.Json.JsonSerializer.Serialize(await fake.RelayStatusAsync()));
	}

	[Fact]
	public async Task ModelsRefresh_AnswersTheScriptedCount_AndIsRecorded()
	{
		var fake = new FakeTessynClient { ModelsRefreshCount = 14 };

		var result = await fake.ModelsRefreshAsync();

		Assert.Equal(14, result.Count);
		Assert.Equal(["models.refresh"], fake.Calls);
	}
}
