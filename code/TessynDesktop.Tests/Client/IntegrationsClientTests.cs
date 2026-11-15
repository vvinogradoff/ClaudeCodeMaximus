using System.Text.Json;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Contract;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.Tests.Client;

/// <summary>
/// The package J3 part 2 wrappers of <see cref="TessynClient"/> (MCP registry and OAuth, Telegram token, relay link, model refresh, the read-only
/// <c>avalonia-ui</c> section) and the typed <c>mcp.changed</c> / <c>catalog.changed</c> notifications, against a real loopback daemon.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class IntegrationsClientTests : IDisposable
{
	private readonly SettingsAdminEnv _env = new();

	public void Dispose() => _env.Dispose();

	private TessynClient NewClient(TessynClientOptions? options = null) =>
		new(new Uri(_env.Host.BaseUrl + Constants.Agent.TdcpEndpointPath), TdcpTestHost.Token, options);

	[Fact]
	public async Task Hello_ReportsTheFourCapabilities()
	{
		await using var client = NewClient();

		await client.ConnectAsync();

		foreach (var capability in new[] { "mcp.registry", "telegram.settings", "relay.settings", "models.admin" })
			Assert.Contains(capability, client.ServerInfo!.Capabilities);
	}

	[Fact]
	public async Task AvaloniaUi_IsReadThroughTheTypedSectionCall_AndAnUpdateIsRefused()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var ui = await client.SettingsGetAsync<AvaloniaUiSettingsDto>(RemoteProtocol.SettingsApi.AvaloniaUiSection);
		var ex = await Assert.ThrowsAsync<TessynRpcException>(() => client.SettingsUpdateAsync(RemoteProtocol.SettingsApi.AvaloniaUiSection, new { theme = "Dark" }, ui.Version));

		Assert.Equal(("avalonia-ui", 1L, "Light"), (ui.Section, ui.Version, ui.Values.Theme));
		Assert.Contains(ui.Values.KeyBindings, b => b is { Action: "Send", Binding: "Ctrl+Enter, Shift+Enter" });
		Assert.Equal((120, 10, "pictures"), (ui.Values.Screensaver.TimeoutSeconds, ui.Values.Screensaver.SlideshowIntervalSeconds, Path.GetFileName(ui.Values.Screensaver.Directory)));
		Assert.Equal("#1E3A5F", ui.Values.DarkColors.UserBubbleBackground);
		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, ex.Code);
	}

	[Fact]
	public async Task TheMcpMethods_ListUpsertEnableRemove()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var list = await client.McpListAsync();
		var created = await client.McpUpsertAsync(new McpServerUpsertRequest(
			DisplayName: "Search", Url: "https://search.example.com/mcp", AuthType: McpAuthTypeDto.StaticHeader, StaticHeaderName: "X-Api-Key", StaticHeaderValue: "client-sent-secret-0001"));
		var renamed = await client.McpUpsertAsync(new McpServerUpsertRequest(Id: created.Id, DisplayName: "Search 2"));
		var disabled = await client.McpSetEnabledAsync(created.Id, false);
		var removed = await client.McpRemoveAsync(created.Id);
		var removedAgain = await client.McpRemoveAsync(created.Id);

		Assert.Equal(["mcp1", "mcp2", "mcp3"], list.Servers.Select(s => s.Id));
		Assert.Equal(("Search", McpAuthTypeDto.StaticHeader, true), (created.DisplayName, created.AuthType, created.HasStaticHeaderValue));
		Assert.Equal(("Search 2", true), (renamed.DisplayName, renamed.HasStaticHeaderValue));      // the value was kept, and is still not readable
		Assert.False(disabled.Enabled);
		Assert.True(removed.Removed);
		Assert.False(removedAgain.Removed);
		Assert.Equal(["mcp1", "mcp2", "mcp3"], (await client.McpListAsync()).Servers.Select(s => s.Id));
		Assert.DoesNotContain("client-sent-secret-0001", JsonSerializer.Serialize(await client.McpListAsync(), TdcpJsonOptions.Default));
	}

	[Fact]
	public async Task McpUpsertAsync_AnInvalidRequest_IsAnInvalidParamsError_AndNothingIsAdded()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var ex = await Assert.ThrowsAsync<TessynRpcException>(() =>
			client.McpUpsertAsync(new McpServerUpsertRequest(DisplayName: "evil", Transport: McpTransportDto.Stdio, Command: @"\\10.0.0.5\share\evil.exe")));

		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, ex.Code);
		Assert.Equal(3, (await client.McpListAsync()).Servers.Count);
	}

	[Fact]
	public async Task McpDiscoverAsync_SendsTheKnownEndpoints_AndAnswersTheConfig()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var result = await client.McpDiscoverAsync("https://tracker.example.com/mcp", new McpDiscoverKnownEndpoints(TokenEndpoint: "https://mine.example.com/token"));

		Assert.Equal(("https://auth.example.com/authorize", "https://mine.example.com/token", false), (result.Config.AuthorizationEndpoint, result.Config.TokenEndpoint, result.TimedOut));
		Assert.Equal(["https://tracker.example.com/mcp"], _env.McpOAuth.Discovered);
	}

	[Fact]
	public async Task McpAuthorizeAsync_WaitsLongerThanTheRequestTimeout_ForTheBrowserFlow()
	{
		await using var client = NewClient(new TessynClientOptions { RequestTimeout = TimeSpan.FromSeconds(2) });
		await client.ConnectAsync();
		_env.McpOAuth.OnAuthorize = async (_, _) =>
		{
			await Task.Delay(3000);   // the user is in the browser: longer than the request timeout above
			return new Models.McpAuthorizeResult(true, "Authorized.");
		};

		var result = await client.McpAuthorizeAsync("mcp2");

		Assert.Equal(new McpAuthorizeResultDto(true), result);
	}

	[Fact]
	public async Task TheTypedNotifications_ReportMcpChanged_AndCatalogChanged()
	{
		await using var client = NewClient();
		var probe = new ClientProbe(client);
		await client.ConnectAsync();

		await client.McpSetEnabledAsync("mcp1", false);
		var mcp = await NextAsync(probe, n => n.TryGetMcpChanged(out _));
		Assert.True(mcp.TryGetMcpChanged(out var payload));
		Assert.Equal(new McpChangedPayload("mcp1"), payload);
		Assert.False(mcp.IsCatalogChanged());

		await client.ModelsRefreshAsync();
		var catalog = await NextAsync(probe, n => n.IsCatalogChanged());
		Assert.False(catalog.TryGetMcpChanged(out _));
	}

	[Fact]
	public async Task TheTelegramMethods_StatusSetTokenClearToken()
	{
		await using var client = NewClient();
		await client.ConnectAsync();
		_env.Settings.Settings.TelegramEnabled = true;
		_env.TelegramTokens.Token = null;

		var before = await client.TelegramStatusAsync();
		var set = await client.TelegramSetTokenAsync("555666777:AAEclientTokenForTheTest_0123456789ab");
		var cleared = await client.TelegramClearTokenAsync();

		Assert.Equal(new TelegramStatusDto(true, false, false), before);
		Assert.Equal(new TelegramStatusDto(true, true, true), set);
		Assert.Equal(new TelegramStatusDto(true, false, false), cleared);
		var ex = await Assert.ThrowsAsync<TessynRpcException>(() => client.TelegramSetTokenAsync("not a token"));
		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, ex.Code);
		Assert.DoesNotContain("not a token", ex.Message);
	}

	[Fact]
	public async Task TheRelayMethods_StatusAndConfigure()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var status = await client.RelayStatusAsync();
		var configured = await client.RelayConfigureAsync(new RelayConfigureRequest(Enabled: false));
		var ex = await Assert.ThrowsAsync<TessynRpcException>(() => client.RelayConfigureAsync(new RelayConfigureRequest(Url: "https://other.example.com")));

		Assert.Equal((true, RelayStateDto.Connected, "https://web.example.com", true), (status.Enabled, status.State, status.Url, status.HasToken));
		Assert.False(configured.Enabled);
		Assert.True(configured.HasToken);
		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, ex.Code);
	}

	[Fact]
	public async Task ModelsRefreshAsync_AsksAgain_AndAnswersTheCatalogSize()
	{
		await using var client = NewClient();
		await client.ConnectAsync();
		_env.ModelCatalog.AfterRefresh = [new("gemma4:26b", string.Empty, "gemma4:26b", Models.ModelProvider.Ollama)];

		var result = await client.ModelsRefreshAsync();

		Assert.Equal(3, result.Count);
		Assert.Equal(1, _env.ModelCatalog.Refreshes);
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
