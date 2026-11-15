using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// The External MCP Server Registry service (package J3 part 2): the DTO mapping that keeps secrets out, validated writes that apply all or nothing, the
/// binding of a stored secret to its origin, the copy-on-write list the turn path reads, the discovery limit and one authorization at a time.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class McpRegistryServiceTests : IDisposable
{
	private const string HeaderSecret = "static-header-secret-value-0001";
	private const string ClientSecret = "oauth-client-secret-value-0002";

	private readonly string _dir;
	private readonly AppSettingsService _real;
	private readonly ThrowingSaveAppSettings _settings;
	private readonly InMemoryMcpTokenStore _tokens = new();
	private readonly ScriptedMcpOAuth _oauth;
	private readonly RemoteEventBus _bus = new();
	private readonly McpRegistryService _service;
	private readonly System.Collections.Generic.List<McpChangedPayload> _changes = [];

	public McpRegistryServiceTests()
	{
		_dir = Path.Combine(Path.GetTempPath(), "mcp-registry-" + Guid.NewGuid().ToString("N")[..8]);
		Directory.CreateDirectory(_dir);
		_real     = new AppSettingsService(_dir);
		_settings = new ThrowingSaveAppSettings(_real);
		_oauth    = new ScriptedMcpOAuth(_tokens);
		_service  = new McpRegistryService(_settings, _tokens, _oauth, _bus, discoverTimeout: TimeSpan.FromMilliseconds(300), authorizeTimeout: TimeSpan.FromMilliseconds(400));
		_bus.Published += n =>
		{
			if (n.Method == RemoteProtocol.McpApi.ChangedEvent)
				lock (_changes) _changes.Add(n.Params.Deserialize<McpChangedPayload>(TdcpJsonOptions.Default)!);
		};
	}

	public void Dispose()
	{
		_bus.Dispose();
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, recursive: true);
	}

	private McpServerDto AddHeaderServer(string id = "s1") =>
		_service.Upsert(new McpServerUpsertRequest(Id: id, DisplayName: "Docs", Url: "https://docs.example.com/mcp", AuthType: McpAuthTypeDto.StaticHeader,
			StaticHeaderName: "Authorization", StaticHeaderValue: HeaderSecret));

	private McpServerDto AddOAuthServer(string id = "o1")
	{
		var dto = _service.Upsert(new McpServerUpsertRequest(Id: id, DisplayName: "Tracker", Url: "https://tracker.example.com/mcp", AuthType: McpAuthTypeDto.Oauth2,
			Oauth: new McpOAuthPatch(AuthorizationEndpoint: "https://auth.example.com/authorize", TokenEndpoint: "https://auth.example.com/token", ClientId: "c1", ClientSecret: ClientSecret)));
		_tokens.Save(id, new McpTokenRecord { AccessToken = "access", RefreshToken = "refresh" });
		return dto;
	}

	private ExternalMcpServerModel Stored(string id) => _settings.Settings.ExternalMcpServers.Single(s => s.Id == id);

	// ── Create and update ────────────────────────────────────────────────────────────────────

	[Fact]
	public void Upsert_WithoutAnId_CreatesAServerWithAGeneratedId_AndSavesAndPublishes()
	{
		var dto = _service.Upsert(new McpServerUpsertRequest(DisplayName: "  New  ", Url: "https://new.example.com/mcp"));

		Assert.Matches("^[0-9a-f]{32}$", dto.Id);
		Assert.Equal(("New", true, McpTransportDto.Http, "https://new.example.com/mcp"), (dto.DisplayName, dto.Enabled, dto.Transport, dto.Url));
		var reloaded = new AppSettingsService(_dir);
		reloaded.Load();
		Assert.Equal(dto.Id, reloaded.Settings.ExternalMcpServers.Single().Id);       // saved to the file
		Assert.Equal([dto.Id], _changes.Select(c => c.Id));
	}

	[Fact]
	public void Upsert_OfAnUnknownId_CreatesItWithThatId_AndOfAKnownOne_UpdatesInPlace()
	{
		var created = _service.Upsert(new McpServerUpsertRequest(Id: "mine-1", DisplayName: "A", Url: "https://a.example.com/mcp"));
		var live = Stored("mine-1");

		var updated = _service.Upsert(new McpServerUpsertRequest(Id: "mine-1", DisplayName: "B"));

		Assert.Equal(("A", "B"), (created.DisplayName, updated.DisplayName));
		Assert.Single(_settings.Settings.ExternalMcpServers);
		Assert.Same(live, Stored("mine-1"));                                           // the Avalonia rows hold the live object
		Assert.Equal("B", live.DisplayName);
		Assert.Equal("https://a.example.com/mcp", live.Url);                           // not in the request: kept
	}

	[Fact]
	public void Upsert_ReplacesTheListInsteadOfEditingIt_OnAdd()
	{
		_service.Upsert(new McpServerUpsertRequest(Id: "a", DisplayName: "A", Url: "https://a.example.com/mcp"));
		var held = _settings.Settings.ExternalMcpServers;                              // what a turn enumerates without a lock

		_service.Upsert(new McpServerUpsertRequest(Id: "b", DisplayName: "B", Url: "https://b.example.com/mcp"));

		Assert.Single(held);
		Assert.Equal(2, _settings.Settings.ExternalMcpServers.Count);
	}

	[Fact]
	public void Remove_ReplacesTheList_DeletesTheToken_AndSaysWhetherAnythingWasThere()
	{
		AddOAuthServer("o1");
		var held = _settings.Settings.ExternalMcpServers;

		var removed = _service.Remove("o1");
		var again = _service.Remove("o1");

		Assert.True(removed);
		Assert.False(again);
		Assert.Single(held);
		Assert.Empty(_settings.Settings.ExternalMcpServers);
		Assert.Contains("o1", _tokens.Deleted);
		Assert.Null(_tokens.Load("o1"));
	}

	[Fact]
	public void SetEnabled_ChangesTheLiveServer_AndRefusesAnUnknownId()
	{
		AddHeaderServer();

		var dto = _service.SetEnabled("s1", false);

		Assert.False(dto.Enabled);
		Assert.False(Stored("s1").Enabled);
		Assert.Throws<TdcpInvalidParamsException>(() => _service.SetEnabled("nope", true));
	}

	[Fact]
	public void AServerNeverInheritsATokenAnEarlierServerOfTheSameIdLeftBehind()
	{
		_tokens.Save("reused", new McpTokenRecord { AccessToken = "old-owner" });

		var dto = _service.Upsert(new McpServerUpsertRequest(Id: "reused", DisplayName: "New owner", Url: "https://new.example.com/mcp", AuthType: McpAuthTypeDto.Oauth2));

		Assert.Null(_tokens.Load("reused"));
		Assert.Equal((McpServerStatusDto.NotConnected, false), (dto.Status, dto.Authorized));
	}

	[Fact]
	public void TheLimit_OnTheNumberOfServers_IsEnforced()
	{
		for (var i = 0; i < Constants.IntegrationsApi.MaxMcpServers; i++)
			_service.Upsert(new McpServerUpsertRequest(Id: "s" + i, DisplayName: "S", Url: "https://s.example.com/mcp"));

		Assert.Throws<TdcpInvalidParamsException>(() => _service.Upsert(new McpServerUpsertRequest(DisplayName: "one more", Url: "https://s.example.com/mcp")));
		Assert.Equal(Constants.IntegrationsApi.MaxMcpServers, _settings.Settings.ExternalMcpServers.Count);
	}

	// ── Secrets are write-only ───────────────────────────────────────────────────────────────

	[Fact]
	public void NoDto_CarriesASecret_AndTheWireJson_DoesNotEither()
	{
		AddHeaderServer("s1");
		AddOAuthServer("o1");
		_service.Upsert(new McpServerUpsertRequest(Id: "q1", DisplayName: "Query", Url: "https://q.example.com/mcp?apikey=query-secret-0003"));
		_service.Upsert(new McpServerUpsertRequest(Id: "x1", DisplayName: "Stdio", Transport: McpTransportDto.Stdio, Command: "npx", Arguments: "--key=argument-secret-0004"));

		var json = JsonSerializer.Serialize(_service.List(), TdcpJsonOptions.Default);

		foreach (var secret in new[] { HeaderSecret, ClientSecret, "access", "refresh", "query-secret-0003", "argument-secret-0004", "apikey" })
			Assert.DoesNotContain(secret, json);
		var dtos = _service.List();
		Assert.True(dtos.Single(d => d.Id == "s1").HasStaticHeaderValue);
		Assert.True(dtos.Single(d => d.Id == "o1").Oauth!.HasClientSecret);
		Assert.True(dtos.Single(d => d.Id == "q1").UrlHasQuery);
		Assert.True(dtos.Single(d => d.Id == "x1").HasArguments);
	}

	[Fact]
	public void AUrlWithUserInfoThatIsAlreadyStored_IsNeverShown()
	{
		_settings.Settings.ExternalMcpServers.Add(new ExternalMcpServerModel { Id = "legacy", DisplayName = "L", Url = "https://user:pw-in-url-0005@legacy.example.com/mcp?k=v#frag" });

		var dto = _service.List().Single();

		Assert.Equal("https://legacy.example.com/mcp", dto.Url);
		Assert.DoesNotContain("pw-in-url-0005", JsonSerializer.Serialize(dto, TdcpJsonOptions.Default));
	}

	[Fact]
	public void AnEmptyString_ClearsASecret_AndALeftOutMember_KeepsIt()
	{
		AddHeaderServer();

		_service.Upsert(new McpServerUpsertRequest(Id: "s1", DisplayName: "Docs 2"));
		Assert.Equal(HeaderSecret, Stored("s1").StaticHeaderValue);

		var cleared = _service.Upsert(new McpServerUpsertRequest(Id: "s1", StaticHeaderValue: string.Empty));
		Assert.Equal(string.Empty, Stored("s1").StaticHeaderValue);
		Assert.False(cleared.HasStaticHeaderValue);
	}

	[Fact]
	public void TheClientSecret_IsWriteOnly_AndAnEmptyOneClearsIt()
	{
		AddOAuthServer();

		_service.Upsert(new McpServerUpsertRequest(Id: "o1", Oauth: new McpOAuthPatch(Scopes: "read")));
		Assert.Equal(ClientSecret, Stored("o1").OAuth!.ClientSecret);

		var cleared = _service.Upsert(new McpServerUpsertRequest(Id: "o1", Oauth: new McpOAuthPatch(ClientSecret: string.Empty)));
		Assert.Null(Stored("o1").OAuth!.ClientSecret);
		Assert.False(cleared.Oauth!.HasClientSecret);
	}

	// ── A secret stays with the origin it was given for ──────────────────────────────────────

	[Fact]
	public void MovingAHeaderServerToAnotherHost_NeedsTheHeaderValueAgain_AndChangesNothingOtherwise()
	{
		AddHeaderServer();

		Assert.Throws<TdcpInvalidParamsException>(() => _service.Upsert(new McpServerUpsertRequest(Id: "s1", Url: "https://evil.example.com/mcp")));
		Assert.Equal("https://docs.example.com/mcp", Stored("s1").Url);                // nothing applied
		Assert.Equal(HeaderSecret, Stored("s1").StaticHeaderValue);

		_service.Upsert(new McpServerUpsertRequest(Id: "s1", Url: "https://new-host.example.com/mcp", StaticHeaderValue: "fresh-value"));
		Assert.Equal(("https://new-host.example.com/mcp", "fresh-value"), (Stored("s1").Url, Stored("s1").StaticHeaderValue));
	}

	[Fact]
	public void MovingAHeaderServerWithinTheSameOrigin_KeepsTheValue()
	{
		AddHeaderServer();

		_service.Upsert(new McpServerUpsertRequest(Id: "s1", Url: "https://DOCS.example.com/v2/mcp"));

		Assert.Equal(HeaderSecret, Stored("s1").StaticHeaderValue);
	}

	[Theory]
	[InlineData("https://other.example.com/mcp", null)]          // the server moves
	[InlineData(null, "https://other-auth.example.com/token")]   // the token endpoint moves: the refresh token would be sent there
	public void MovingAnOAuthServerOrItsTokenEndpoint_DeletesTheTokenAndTheClientSecret(string? url, string? tokenEndpoint)
	{
		AddOAuthServer();
		Stored("o1").OAuth!.NeedsReauthorization = true;
		Stored("o1").OAuth!.ConnectedIdentity = "someone";

		var dto = _service.Upsert(new McpServerUpsertRequest(Id: "o1", Url: url, Oauth: tokenEndpoint == null ? null : new McpOAuthPatch(TokenEndpoint: tokenEndpoint)));

		Assert.Null(_tokens.Load("o1"));
		Assert.Null(Stored("o1").OAuth!.ClientSecret);
		Assert.False(Stored("o1").OAuth!.NeedsReauthorization);
		Assert.Null(dto.ConnectedIdentity);
		Assert.Equal(McpServerStatusDto.NotConnected, dto.Status);
	}

	[Fact]
	public void MovingAnOAuthServer_AndSendingTheClientSecretAgain_KeepsTheNewSecret()
	{
		AddOAuthServer();

		_service.Upsert(new McpServerUpsertRequest(Id: "o1", Url: "https://other.example.com/mcp", Oauth: new McpOAuthPatch(ClientSecret: "new-client-secret")));

		Assert.Null(_tokens.Load("o1"));
		Assert.Equal("new-client-secret", Stored("o1").OAuth!.ClientSecret);
	}

	[Fact]
	public void ChangingTheEndpointsWithinTheirOrigins_KeepsTheToken()
	{
		AddOAuthServer();

		_service.Upsert(new McpServerUpsertRequest(Id: "o1", Oauth: new McpOAuthPatch(TokenEndpoint: "https://auth.example.com/v2/token", Scopes: "read")));

		Assert.NotNull(_tokens.Load("o1"));
		Assert.Equal(ClientSecret, Stored("o1").OAuth!.ClientSecret);
	}

	[Fact]
	public void LeavingTheAuthType_DropsTheSecretsOfTheOldOne()
	{
		AddOAuthServer();
		AddHeaderServer();

		_service.Upsert(new McpServerUpsertRequest(Id: "o1", AuthType: McpAuthTypeDto.None));
		_service.Upsert(new McpServerUpsertRequest(Id: "s1", AuthType: McpAuthTypeDto.None));

		Assert.Null(Stored("o1").OAuth);
		Assert.Null(_tokens.Load("o1"));
		Assert.Equal((string.Empty, string.Empty), (Stored("s1").StaticHeaderName, Stored("s1").StaticHeaderValue));
	}

	[Fact]
	public void SwitchingTheTransport_DropsTheMembersOfTheOldOne()
	{
		AddOAuthServer();
		_service.Upsert(new McpServerUpsertRequest(Id: "x1", DisplayName: "S", Transport: McpTransportDto.Stdio, Command: "npx", Arguments: "--key=k", WorkingDirectory: @"C:\work"));

		var toStdio = _service.Upsert(new McpServerUpsertRequest(Id: "o1", Transport: McpTransportDto.Stdio, Command: "npx"));
		var toHttp = _service.Upsert(new McpServerUpsertRequest(Id: "x1", Transport: McpTransportDto.Http, Url: "https://h.example.com/mcp"));

		Assert.Equal((string.Empty, McpServerAuthType.None, (McpOAuthConfig?)null), (Stored("o1").Url, Stored("o1").AuthType, Stored("o1").OAuth));
		Assert.Null(_tokens.Load("o1"));
		Assert.Equal(McpTransportDto.Stdio, toStdio.Transport);
		Assert.Equal((string.Empty, string.Empty, string.Empty), (Stored("x1").Command, Stored("x1").Arguments, Stored("x1").WorkingDirectory));
		Assert.False(toHttp.HasArguments);
	}

	// ── Validation: all or nothing ───────────────────────────────────────────────────────────

	[Theory]
	[InlineData(@"\\10.0.0.5\share\mcp.exe")]
	[InlineData(@"\\?\C:\mcp.exe")]
	[InlineData(@"C:mcp.exe")]
	[InlineData(@"..\mcp.exe")]
	[InlineData(@"tools\mcp.exe")]
	[InlineData("mcp.exe\r\nother")]
	public void AStdioCommand_ThatIsNotABareNameOrAFullyQualifiedLocalPath_IsRefused_AndNothingIsSaved(string command)
	{
		var request = new McpServerUpsertRequest(DisplayName: "S", Transport: McpTransportDto.Stdio, Command: command);

		Assert.Throws<TdcpInvalidParamsException>(() => _service.Upsert(request));

		Assert.Empty(_settings.Settings.ExternalMcpServers);
		Assert.False(File.Exists(Path.Combine(_dir, "appsettings.json")));
	}

	[Theory]
	[InlineData("npx")]
	[InlineData("roslyn-mcp.exe")]
	[InlineData(@"C:\Tools\mcp\server.exe")]
	public void AStdioCommand_ThatIsABareNameOrAFullyQualifiedLocalPath_IsAccepted(string command)
	{
		var dto = _service.Upsert(new McpServerUpsertRequest(DisplayName: "S", Transport: McpTransportDto.Stdio, Command: command, WorkingDirectory: @"D:\work"));

		Assert.Equal((command, @"D:\work"), (dto.Command, dto.WorkingDirectory));
	}

	[Fact]
	public void ARejectedUpdate_ChangesNothing_NotEvenTheMembersBeforeTheBadOne()
	{
		AddHeaderServer();
		var before = JsonSerializer.Serialize(_settings.Settings.ExternalMcpServers);

		Assert.Throws<TdcpInvalidParamsException>(() => _service.Upsert(new McpServerUpsertRequest(Id: "s1", DisplayName: "renamed", Enabled: false, StaticHeaderName: "bad name")));

		Assert.Equal(before, JsonSerializer.Serialize(_settings.Settings.ExternalMcpServers));
	}

	[Fact]
	public void AFailingSave_PutsTheOldValuesBack_ForCreateUpdateRemoveAndSetEnabled()
	{
		AddHeaderServer();
		var before = JsonSerializer.Serialize(_settings.Settings.ExternalMcpServers);
		var heldList = _settings.Settings.ExternalMcpServers;
		_settings.FailSaves = true;

		Assert.Throws<IOException>(() => _service.Upsert(new McpServerUpsertRequest(DisplayName: "new", Url: "https://n.example.com/mcp")));
		Assert.Throws<IOException>(() => _service.Upsert(new McpServerUpsertRequest(Id: "s1", DisplayName: "changed", Enabled: false)));
		Assert.Throws<IOException>(() => _service.SetEnabled("s1", false));
		Assert.Throws<IOException>(() => _service.Remove("s1"));

		Assert.Equal(before, JsonSerializer.Serialize(_settings.Settings.ExternalMcpServers));
		Assert.Same(heldList, _settings.Settings.ExternalMcpServers);
		_settings.FailSaves = false;
		Assert.Equal("changed", _service.Upsert(new McpServerUpsertRequest(Id: "s1", DisplayName: "changed")).DisplayName);   // and the service still works
	}

	[Fact]
	public void TheChangedNotification_IsPublishedAfterASuccessfulWriteOnly()
	{
		AddHeaderServer();
		_changes.Clear();
		_settings.FailSaves = true;
		Assert.Throws<IOException>(() => _service.SetEnabled("s1", false));
		Assert.Throws<TdcpInvalidParamsException>(() => _service.Upsert(new McpServerUpsertRequest(DisplayName: "")));

		Assert.Empty(_changes);
	}

	// ── Discovery and authorization ──────────────────────────────────────────────────────────

	[Fact]
	public async Task Discover_KeepsTheEndpointsTheCallerKnew_AndFillsTheRest()
	{
		var result = await _service.DiscoverAsync("https://tracker.example.com/mcp", new McpDiscoverKnownEndpoints(AuthorizationEndpoint: "https://mine.example.com/authorize"), CancellationToken.None);

		Assert.Equal(("https://mine.example.com/authorize", "https://auth.example.com/token", "https://auth.example.com/register", true, false),
			(result.Config.AuthorizationEndpoint, result.Config.TokenEndpoint, result.Config.RegistrationEndpoint, result.Config.DiscoveryCompleted, result.TimedOut));
	}

	[Theory]
	[InlineData("file:///c:/x")]
	[InlineData("https://user:pw@h.example.com")]
	[InlineData("not a url")]
	public async Task Discover_RefusesAnUrlThatIsNotHttpWithoutUserInfo(string url)
	{
		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => _service.DiscoverAsync(url, null, CancellationToken.None));

		Assert.Empty(_oauth.Discovered);
	}

	[Fact]
	public async Task Discover_ReturnsWhatItHasAfterTheTimeout_AndTheAbandonedProbeIsCancelled()
	{
		var sawCancel = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_oauth.OnDiscover = async (_, oauth, ct) =>
		{
			oauth.AuthorizationEndpoint = "https://found-so-far.example.com/authorize";
			try { await Task.Delay(Timeout.Infinite, ct); }
			catch (OperationCanceledException) { sawCancel.SetResult(); throw; }
		};

		var result = await _service.DiscoverAsync("https://slow.example.com/mcp", null, CancellationToken.None);

		Assert.True(result.TimedOut);
		Assert.Equal("https://found-so-far.example.com/authorize", result.Config.AuthorizationEndpoint);
		Assert.True(await Task.WhenAny(sawCancel.Task, Task.Delay(3000)) == sawCancel.Task);
	}

	[Fact]
	public async Task Discover_StopsWhenTheCallerCancels_WithoutReportingATimeout()
	{
		_oauth.OnDiscover = (_, _, _) => new TaskCompletionSource().Task;
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.DiscoverAsync("https://slow.example.com/mcp", null, cts.Token));
	}

	[Fact]
	public async Task Authorize_OfAnUnknownServer_IsInvalidParams_AndOfARunningOne_IsAnErrorResult()
	{
		AddOAuthServer();
		var gate = new TaskCompletionSource<McpAuthorizeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		_oauth.OnAuthorize = (_, _) => gate.Task;

		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => _service.AuthorizeAsync("nope", CancellationToken.None));
		var first = _service.AuthorizeAsync("o1", CancellationToken.None);
		var second = await _service.AuthorizeAsync("o1", CancellationToken.None);
		gate.SetResult(new McpAuthorizeResult(true, "Authorized."));

		Assert.False(second.Authorized);
		Assert.True((await first).Authorized);
		Assert.Single(_oauth.Authorized);
		Assert.True((await _service.AuthorizeAsync("o1", CancellationToken.None)).Authorized);   // the guard is released
	}

	[Fact]
	public async Task Authorize_PassesTheLiveModel_ReportsTheFailureText_AndNeverATokenOrASecret()
	{
		AddOAuthServer();
		ExternalMcpServerModel? seen = null;
		_oauth.OnAuthorize = (server, _) =>
		{
			seen = server;
			return Task.FromResult(new McpAuthorizeResult(false, "Token exchange failed."));
		};

		var result = await _service.AuthorizeAsync("o1", CancellationToken.None);

		Assert.Same(Stored("o1"), seen);
		Assert.Equal(new McpAuthorizeResultDto(false, "Token exchange failed."), result);
	}

	[Fact]
	public async Task Authorize_OfAServerRemovedMeanwhile_DeletesTheToken()
	{
		AddOAuthServer();
		_oauth.OnAuthorize = (server, _) =>
		{
			_service.Remove(server.Id);
			_tokens.Save(server.Id, new McpTokenRecord { AccessToken = "late" });
			return Task.FromResult(new McpAuthorizeResult(true, "Authorized."));
		};

		var result = await _service.AuthorizeAsync("o1", CancellationToken.None);

		Assert.False(result.Authorized);
		Assert.Null(_tokens.Load("o1"));
	}

	[Fact]
	public void TheStatus_FollowsTheToken_AndTheReauthorizationFlag()
	{
		AddOAuthServer();

		Assert.Equal(McpServerStatusDto.Connected, _service.List().Single().Status);

		Stored("o1").OAuth!.NeedsReauthorization = true;
		Assert.Equal((McpServerStatusDto.NeedsReauthorization, false), (_service.List().Single().Status, _service.List().Single().Authorized));

		Stored("o1").OAuth!.NeedsReauthorization = false;
		_tokens.Delete("o1");
		Assert.Equal(McpServerStatusDto.NotConnected, _service.List().Single().Status);
	}

	// ── Review fixes (447) ───────────────────────────────────────────────────────────────────

	[Theory]
	[InlineData("x\n")]
	[InlineData("x\r\n")]
	[InlineData("\nx")]
	[InlineData("x y")]
	[InlineData("")]
	public void M1_AnIdWithALineBreakOrAnyOtherStrayCharacter_IsRefused_AndNothingIsStored(string id)
	{
		Assert.Throws<TdcpInvalidParamsException>(() => _service.Upsert(new McpServerUpsertRequest(Id: id, DisplayName: "x", Url: "https://a.example.com/mcp")));

		Assert.Empty(_settings.Settings.ExternalMcpServers);
	}

	[Fact]
	public void M1_ThePatternsOfAFileNameAHeaderAndATokenUseAnchorsThatDoNotAcceptATrailingLineBreak()
	{
		Assert.False(System.Text.RegularExpressions.Regex.IsMatch("x\n", Constants.IntegrationsApi.McpIdPattern));
		Assert.False(System.Text.RegularExpressions.Regex.IsMatch("X-Api-Key\n", Constants.IntegrationsApi.HeaderNamePattern));
		Assert.False(System.Text.RegularExpressions.Regex.IsMatch("123456789:AAEtokenForTheTest_0123456789ab\n", Constants.IntegrationsApi.TelegramTokenPattern));
		Assert.False(System.Text.RegularExpressions.Regex.IsMatch("link-token-0001\n", Constants.IntegrationsApi.LinkTokenPattern));
		Assert.True(System.Text.RegularExpressions.Regex.IsMatch("x_1-Z", Constants.IntegrationsApi.McpIdPattern));          // and the good ones still match
		Assert.True(System.Text.RegularExpressions.Regex.IsMatch("X-Api-Key", Constants.IntegrationsApi.HeaderNamePattern));
		Assert.True(System.Text.RegularExpressions.Regex.IsMatch("123456789:AAEtokenForTheTest_0123456789ab", Constants.IntegrationsApi.TelegramTokenPattern));
		Assert.True(System.Text.RegularExpressions.Regex.IsMatch("link-token-0001", Constants.IntegrationsApi.LinkTokenPattern));
	}

	[Fact]
	public void M2_ATokenEndpointThatAppearsWhereThereWasNone_DropsTheStoredClientSecret()
	{
		_service.Upsert(new McpServerUpsertRequest(Id: "o2", DisplayName: "No endpoint yet", Url: "https://tracker.example.com/mcp", AuthType: McpAuthTypeDto.Oauth2,
			Oauth: new McpOAuthPatch(ClientId: "c1", ClientSecret: ClientSecret)));
		Assert.Equal(string.Empty, Stored("o2").OAuth!.TokenEndpoint);                // the Avalonia dialog saves before discovery completes

		_service.Upsert(new McpServerUpsertRequest(Id: "o2", Oauth: new McpOAuthPatch(TokenEndpoint: "https://attacker.example.com/token")));

		Assert.Null(Stored("o2").OAuth!.ClientSecret);
		Assert.Equal("https://attacker.example.com/token", Stored("o2").OAuth!.TokenEndpoint);
	}

	[Fact]
	public void M2_AndKeepsTheSecretOnlyWhenANewOneComesInTheSameCall_OrNothingMoved()
	{
		_service.Upsert(new McpServerUpsertRequest(Id: "o2", DisplayName: "No endpoint yet", Url: "https://tracker.example.com/mcp", AuthType: McpAuthTypeDto.Oauth2,
			Oauth: new McpOAuthPatch(ClientId: "c1", ClientSecret: ClientSecret)));

		_service.Upsert(new McpServerUpsertRequest(Id: "o2", Oauth: new McpOAuthPatch(Scopes: "read")));             // no endpoint on either side: nothing moved
		Assert.Equal(ClientSecret, Stored("o2").OAuth!.ClientSecret);

		_service.Upsert(new McpServerUpsertRequest(Id: "o2", Oauth: new McpOAuthPatch(TokenEndpoint: "https://auth.example.com/token", ClientSecret: "fresh-secret")));
		Assert.Equal("fresh-secret", Stored("o2").OAuth!.ClientSecret);
	}

	[Fact]
	public async Task L2_AnAuthorizationThatNeverEnds_IsStoppedAfterTheCap_AndTheGuardIsReleased()
	{
		AddOAuthServer();
		var sawCancel = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_oauth.OnAuthorize = async (_, ct) =>
		{
			try { await Task.Delay(Timeout.Infinite, ct); }
			catch (OperationCanceledException) { sawCancel.TrySetResult(); throw; }
			return new McpAuthorizeResult(true, "never");
		};
		var watch = System.Diagnostics.Stopwatch.StartNew();

		var first = await _service.AuthorizeAsync("o1", CancellationToken.None);

		Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
		Assert.False(first.Authorized);
		Assert.Contains("too long", first.Error);
		Assert.True(await Task.WhenAny(sawCancel.Task, Task.Delay(3000)) == sawCancel.Task);   // the abandoned flow was told to stop
		_oauth.OnAuthorize = null;
		Assert.True((await _service.AuthorizeAsync("o1", CancellationToken.None)).Authorized); // a retry is not met by "already running"
	}

	[Fact]
	public async Task L2_AFlowThatIgnoresItsToken_IsStoppedToo()
	{
		AddOAuthServer();
		_oauth.OnAuthorize = (_, _) => new TaskCompletionSource<McpAuthorizeResult>().Task;

		var result = await _service.AuthorizeAsync("o1", CancellationToken.None);

		Assert.Contains("too long", result.Error);
	}

	[Fact]
	public async Task L2_ACallerThatCancels_IsNotToldItTimedOut()
	{
		AddOAuthServer();
		_oauth.OnAuthorize = (_, _) => new TaskCompletionSource<McpAuthorizeResult>().Task;
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.AuthorizeAsync("o1", cts.Token));
	}

	[Fact]
	public async Task L2_AServerRemovedAndRecreatedDuringTheFlow_KeepsTheTokenOfTheNewOne()
	{
		AddOAuthServer();
		_oauth.OnAuthorize = (server, _) =>
		{
			_service.Remove(server.Id);
			AddOAuthServer(server.Id);                                       // same id, a new server, with its own token
			_tokens.Save(server.Id, new McpTokenRecord { AccessToken = "token-of-the-new-server" });
			return Task.FromResult(new McpAuthorizeResult(true, "Authorized."));
		};

		var result = await _service.AuthorizeAsync("o1", CancellationToken.None);

		Assert.False(result.Authorized);
		Assert.Equal("token-of-the-new-server", _tokens.Load("o1")!.AccessToken);
	}

	[Fact]
	public void L4_AReaderOfTheLiveObject_NeverSeesANewUrlWithTheOldSecret()
	{
		const string urlA = "https://a.example.com/mcp", urlB = "https://b.example.com/mcp";
		_service.Upsert(new McpServerUpsertRequest(Id: "live", DisplayName: "Live", Url: urlA, AuthType: McpAuthTypeDto.StaticHeader, StaticHeaderName: "X-Key", StaticHeaderValue: "value-for-a"));
		var live = Stored("live");
		var stop = false;
		var wrong = 0;
		var reader = new Thread(() =>
		{
			// What the turn path does, without the lock: read the value, the URL, the value again; only a value that did not change across the URL read counts.
			while (!Volatile.Read(ref stop))
			{
				var before = live.StaticHeaderValue;
				var url = live.Url;
				var after = live.StaticHeaderValue;
				if (before == after && ((url == urlB && before == "value-for-a") || (url == urlA && before == "value-for-b")))
					Interlocked.Increment(ref wrong);
			}
		}) { IsBackground = true };
		reader.Start();

		for (var i = 0; i < 1500; i++)
		{
			var toB = i % 2 == 0;
			_service.Upsert(new McpServerUpsertRequest(Id: "live", Url: toB ? urlB : urlA, StaticHeaderValue: toB ? "value-for-b" : "value-for-a"));
		}

		Volatile.Write(ref stop, true);
		reader.Join();
		Assert.Equal(0, wrong);
	}

	[Fact]
	public void L6_RemovingAServer_PrunesItFromTheLocalToolProfilesOfTheDirectories()
	{
		AddHeaderServer("keep");
		AddHeaderServer("gone");
		var a = new DirectoryNodeModel { Path = @"C:\a", LocalToolProfile = new LocalToolProfileModel { IncludedExternalMcpServerIds = ["keep", "gone"] } };
		var b = new DirectoryNodeModel { Path = @"C:\b", LocalToolProfile = new LocalToolProfileModel { IncludedExternalMcpServerIds = ["gone"] } };
		var c = new DirectoryNodeModel { Path = @"C:\c" };                                  // no profile at all
		_settings.Settings.Tree.AddRange([a, b, c]);

		_service.Remove("gone");
		_service.Upsert(new McpServerUpsertRequest(Id: "gone", DisplayName: "A different server, same id", Url: "https://other.example.com/mcp"));

		Assert.Equal(["keep"], a.LocalToolProfile!.IncludedExternalMcpServerIds);
		Assert.Empty(b.LocalToolProfile!.IncludedExternalMcpServerIds);
		Assert.Null(c.LocalToolProfile);
		var reloaded = new AppSettingsService(_dir);
		reloaded.Load();
		Assert.Equal(["keep"], reloaded.Settings.Tree[0].LocalToolProfile!.IncludedExternalMcpServerIds);   // saved
	}

	[Fact]
	public void L6_AFailingSave_PutsTheIncludedIdsBack()
	{
		AddHeaderServer("gone");
		var dir = new DirectoryNodeModel { Path = @"C:\a", LocalToolProfile = new LocalToolProfileModel { IncludedExternalMcpServerIds = ["gone"] } };
		_settings.Settings.Tree.Add(dir);
		_settings.FailSaves = true;

		Assert.Throws<IOException>(() => _service.Remove("gone"));

		Assert.Equal(["gone"], dir.LocalToolProfile!.IncludedExternalMcpServerIds);
		Assert.Single(_settings.Settings.ExternalMcpServers);
	}
}
