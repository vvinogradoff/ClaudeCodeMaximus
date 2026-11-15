using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.ViewModels.Settings;

namespace TessynDesktop.WPF.Tests;

/// <summary>The MCP registry, Web access and Models/profiles tabs of the Settings window (W6b): immediate commits, write-only secrets, cancellable discovery, per-row authorize.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SettingsIntegrationsTests
{
	private const string SecretHeader = "Bearer super-secret-header-value";
	private const string SecretClient = "client-secret-value-123";

	private readonly FakeTessynClient _client = new FakeTessynClient().AnnouncingSettings().WithSettingsValues();
	private readonly FakeDialogPresenter _dialogs = new();
	private readonly ScriptedMcpEditorPresenter _editors = new();

	private static McpServerDto Http(string id, string name, McpAuthTypeDto auth = McpAuthTypeDto.None, McpServerStatusDto status = McpServerStatusDto.NotApplicable, bool urlHasQuery = false,
		McpOAuthDto? oauth = null, bool hasHeaderValue = false, bool enabled = true) =>
		new(id, name, enabled, McpTransportDto.Http, $"https://{id}.example.com/mcp", urlHasQuery, null, false, null, auth, auth == McpAuthTypeDto.StaticHeader ? "Authorization" : null, hasHeaderValue, oauth,
			status == McpServerStatusDto.Connected, status, null);

	private static McpServerDto Stdio(string id, string name, bool hasArgs = true) =>
		new(id, name, true, McpTransportDto.Stdio, null, false, "npx", hasArgs, @"C:\work", McpAuthTypeDto.None, null, false, null, false, McpServerStatusDto.NotApplicable, null);

	private McpSectionViewModel NewMcp() => new(_client, new DialogService(_dialogs, new ImmediateUiDispatcher()), _editors, NullLogger<McpSectionViewModel>.Instance);

	private McpServerEditorViewModel NewEditor(McpServerDto? server = null) => new(_client, server, NullLogger.Instance);

	private void ConfirmAll() => _dialogs.Answer = dialog =>
	{
		dialog.AcceptCommand.Execute(null);
		return Task.CompletedTask;
	};

	// ---- the list

	[Fact]
	public async Task Mcp_ListsTheRegistry_WithStatusAndWithoutAnySecret()
	{
		_client.McpServers =
		[
			Http("a", "Linear", McpAuthTypeDto.Oauth2, McpServerStatusDto.Connected),
			Http("b", "Keyed", McpAuthTypeDto.StaticHeader, hasHeaderValue: true, urlHasQuery: true),
			Stdio("c", "Local tool"),
		];
		var mcp = NewMcp();

		await mcp.InitializeAsync();

		Assert.Equal(["Linear", "Keyed", "Local tool"], mcp.Servers.Select(s => s.DisplayName));
		Assert.Equal("Connected", mcp.Servers[0].StatusText);
		Assert.Equal("Re-authorize", mcp.Servers[0].AuthorizeText);
		Assert.True(mcp.Servers[0].CanAuthorize);
		Assert.False(mcp.Servers[1].CanAuthorize);
		Assert.Contains("value stored", mcp.Servers[1].AuthSummary);
		Assert.DoesNotContain("super", mcp.Servers[1].AuthSummary);
		Assert.StartsWith("stdio", mcp.Servers[2].TransportSummary);
		Assert.Contains("…", mcp.Servers[2].TransportSummary);   // arguments exist but are not shown
	}

	[Fact]
	public async Task Mcp_ADaemonWithoutTheRegistry_ShowsANote()
	{
		var client = new FakeTessynClient().AnnouncingSettings(RemoteProtocol.SettingsApi.Capability);
		var mcp = new McpSectionViewModel(client, new DialogService(_dialogs, new ImmediateUiDispatcher()), _editors, NullLogger<McpSectionViewModel>.Instance);

		await mcp.InitializeAsync();

		Assert.False(mcp.IsAvailable);
		Assert.DoesNotContain("mcp.list", client.Calls);
	}

	[Fact]
	public async Task Mcp_AFailingList_IsShown_AndKeepsTheRowsItHad()
	{
		_client.McpServers = [Http("a", "Linear")];
		var mcp = NewMcp();
		await mcp.InitializeAsync();
		_client.McpException = new InvalidOperationException("daemon went away");

		await mcp.RefreshAsync();

		Assert.Equal("daemon went away", mcp.ErrorText);
		Assert.Single(mcp.Servers);
	}

	[Fact]
	public async Task Mcp_RemoveAsksFirst_AndAppliesAtOnce()
	{
		_client.McpServers = [Http("a", "Linear"), Http("b", "Other")];
		var mcp = NewMcp();
		await mcp.InitializeAsync();

		await mcp.RemoveAsync(mcp.Servers[0]);   // cancelled by the fake presenter
		Assert.Equal(2, mcp.Servers.Count);

		ConfirmAll();
		await mcp.RemoveAsync(mcp.Servers[0]);

		Assert.Contains("mcp.remove:a", _client.Calls);
		Assert.Equal(["Other"], mcp.Servers.Select(s => s.DisplayName));
	}

	[Fact]
	public async Task Mcp_Toggle_CallsSetEnabledWithTheOppositeValue()
	{
		_client.McpServers = [Http("a", "Linear", enabled: true)];
		var mcp = NewMcp();
		await mcp.InitializeAsync();

		await mcp.ToggleEnabledAsync(mcp.Servers[0]);

		Assert.Contains("mcp.setEnabled:a:False", _client.Calls);
		Assert.False(mcp.Servers[0].Enabled);
		Assert.Equal("Enable", mcp.Servers[0].ToggleText);
	}

	[Fact]
	public async Task Mcp_AFailedAction_IsShownOnItsRow()
	{
		_client.McpServers = [Http("a", "Linear")];
		var mcp = NewMcp();
		await mcp.InitializeAsync();
		_client.McpException = new TessynRpcException(RemoteProtocol.ErrorCodes.InvalidParams, "There is no MCP server 'a'.");

		await mcp.ToggleEnabledAsync(mcp.Servers[0]);

		Assert.Contains("no MCP server", mcp.Servers[0].Message);
	}

	[Fact]
	public async Task Mcp_ChangedNotification_RefreshesTheList()
	{
		var mcp = NewMcp();
		await mcp.InitializeAsync();
		Assert.Empty(mcp.Servers);
		_client.McpServers = [Http("z", "Added elsewhere")];

		await mcp.OnChangedAsync();

		Assert.Equal(["Added elsewhere"], mcp.Servers.Select(s => s.DisplayName));
	}

	[Fact]
	public async Task Mcp_SequentialRefreshes_EndWithTheLatestList()
	{
		var mcp = NewMcp();
		await mcp.InitializeAsync();
		_client.McpServers = [Http("1", "First")];
		var first = mcp.RefreshAsync();
		_client.McpServers = [Http("2", "Second")];
		await mcp.RefreshAsync();
		await first;

		Assert.Equal(["Second"], mcp.Servers.Select(s => s.DisplayName));
	}

	// ---- authorize

	[Fact]
	public async Task Authorize_ShowsABusyRow_ThenTheOutcome_AndRefreshesTheStatus()
	{
		_client.McpServers = [Http("a", "Linear", McpAuthTypeDto.Oauth2, McpServerStatusDto.NotConnected)];
		var mcp = NewMcp();
		await mcp.InitializeAsync();
		var release = new TaskCompletionSource();
		_client.McpAuthorizeBehavior = () => release.Task;
		var row = mcp.Servers[0];

		var pending = mcp.AuthorizeAsync(row);
		Assert.True(row.IsBusy);
		Assert.Contains("browser", row.Message);

		// A second click while the browser is open does nothing.
		await mcp.AuthorizeAsync(row);
		Assert.Single(_client.Calls, c => c == "mcp.authorize:a");

		_client.McpServers = [Http("a", "Linear", McpAuthTypeDto.Oauth2, McpServerStatusDto.Connected)];
		release.SetResult();
		await pending;

		var current = Assert.Single(mcp.Servers);
		Assert.False(current.IsBusy);
		Assert.Equal("Connected", current.StatusText);
		Assert.Contains("is authorized", current.Message);
	}

	[Fact]
	public async Task Authorize_ARefusal_ShowsTheDaemonsReason()
	{
		_client.McpServers = [Http("a", "Linear", McpAuthTypeDto.Oauth2, McpServerStatusDto.NotConnected)];
		_client.McpAuthorizeAnswer = new McpAuthorizeResultDto(false, "The authorization took too long.");
		var mcp = NewMcp();
		await mcp.InitializeAsync();

		await mcp.AuthorizeAsync(mcp.Servers[0]);

		Assert.Contains("took too long", mcp.Servers[0].Message);
		Assert.False(mcp.Servers[0].IsBusy);
	}

	[Fact]
	public async Task Authorize_AServerWithoutOAuth_HasNothingToAuthorize()
	{
		_client.McpServers = [Http("a", "Plain")];
		var mcp = NewMcp();
		await mcp.InitializeAsync();

		await mcp.AuthorizeAsync(mcp.Servers[0]);

		Assert.DoesNotContain("mcp.authorize:a", _client.Calls);
	}

	// ---- the editor window's view model

	[Fact]
	public async Task Add_ShowsTheEditor_AndTheSavedServerAppearsInTheList()
	{
		var mcp = NewMcp();
		await mcp.InitializeAsync();
		_editors.Script = async editor =>
		{
			editor.DisplayName = "  New one  ";
			editor.Url = "https://new.example.com/mcp";
			Assert.True(await editor.TrySaveAsync());
		};

		await mcp.AddAsync();

		Assert.Equal(["New one"], mcp.Servers.Select(s => s.DisplayName));
		Assert.Single(_client.McpUpserts);
		Assert.Null(_client.McpUpserts[0].Id);   // the daemon makes the id
	}

	[Fact]
	public async Task Add_Cancelled_ChangesNothing()
	{
		var mcp = NewMcp();
		await mcp.InitializeAsync();

		await mcp.AddAsync();   // the scripted presenter closes without saving

		Assert.Empty(_client.McpUpserts);
		Assert.Empty(mcp.Servers);
	}

	[Fact]
	public void Editor_ANewHttpServer_SendsTheWholeShape_WithSecretsOnlyWhenTyped()
	{
		var editor = NewEditor();
		editor.DisplayName = "Keyed";
		editor.Url = "https://keyed.example.com/mcp";
		editor.AuthType = McpAuthTypeDto.StaticHeader;
		editor.StaticHeaderName = "X-Api-Key";
		editor.StaticHeaderValueInput = SecretHeader;

		var request = editor.BuildRequest();

		Assert.Null(request.Id);
		Assert.Equal(McpTransportDto.Http, request.Transport);
		Assert.Equal("https://keyed.example.com/mcp", request.Url);
		Assert.Equal("X-Api-Key", request.StaticHeaderName);
		Assert.Equal(SecretHeader, request.StaticHeaderValue);
		Assert.Null(request.Command);
		Assert.Null(request.Oauth);
	}

	[Fact]
	public void Editor_EditingAServer_SendsOnlyWhatChanged_AndNeverTheStoredSecretsOrQuery()
	{
		var stored = Http("a", "Keyed", McpAuthTypeDto.StaticHeader, hasHeaderValue: true, urlHasQuery: true);
		var editor = NewEditor(stored);
		Assert.Equal(string.Empty, editor.StaticHeaderValueInput);   // write-only: the stored value is never in the field
		Assert.Contains("stored", editor.HeaderValueText);
		Assert.True(editor.UrlHasHiddenQuery);

		editor.DisplayName = "Renamed";
		var request = editor.BuildRequest();

		Assert.Equal("a", request.Id);
		Assert.Equal("Renamed", request.DisplayName);
		Assert.Null(request.Url);               // the URL (with its hidden query) is left alone
		Assert.Null(request.StaticHeaderValue); // an empty secret field keeps the stored value
		Assert.Null(request.StaticHeaderName);
		Assert.Null(request.AuthType);
		Assert.Null(request.Enabled);
	}

	[Fact]
	public void Editor_TypingANewSecret_ReplacesIt()
	{
		var editor = NewEditor(Http("a", "Keyed", McpAuthTypeDto.StaticHeader, hasHeaderValue: true));
		editor.StaticHeaderValueInput = "new-value";

		Assert.Equal("new-value", editor.BuildRequest().StaticHeaderValue);
	}

	[Fact]
	public void Editor_AStdioServer_SendsItsCommand_AndKeepsStoredArgumentsUnlessReplacedOrCleared()
	{
		var stored = Stdio("c", "Local tool");
		var editor = NewEditor(stored);
		Assert.True(editor.HasStoredArguments);
		editor.Command = "node";
		Assert.Null(editor.BuildRequest().Arguments);

		editor.ArgumentsInput = "server.js --port 1";
		Assert.Equal("server.js --port 1", editor.BuildRequest().Arguments);

		editor.ArgumentsInput = string.Empty;
		editor.ClearArguments = true;
		Assert.Equal(string.Empty, editor.BuildRequest().Arguments);   // an empty string clears
		Assert.Equal("node", editor.BuildRequest().Command);
		Assert.Contains("command Claude starts", editor.StdioWarning);
	}

	[Theory]
	[InlineData("", "https://x.example.com", "A display name is required.")]
	[InlineData("n", "", "An http(s) URL is required.")]
	[InlineData("n", "ftp://x.example.com", "An http(s) URL is required.")]
	[InlineData("n", "not a url", "An http(s) URL is required.")]
	public async Task Editor_InvalidEntries_AreRefusedBeforeAnythingIsSent(string name, string url, string expected)
	{
		var editor = NewEditor();
		editor.DisplayName = name;
		editor.Url = url;

		Assert.False(await editor.TrySaveAsync());

		Assert.Equal(expected, editor.ErrorText);
		Assert.Empty(_client.McpUpserts);
		Assert.False(editor.Saved);
	}

	[Fact]
	public async Task Editor_AStaticHeaderServerNeedsAHeaderName_AStdioServerACommand()
	{
		var keyed = NewEditor();
		keyed.DisplayName = "n";
		keyed.Url = "https://x.example.com";
		keyed.AuthType = McpAuthTypeDto.StaticHeader;
		Assert.False(await keyed.TrySaveAsync());
		Assert.Equal(Constants.Settings.McpHeaderNameRequired, keyed.ErrorText);

		var local = NewEditor();
		local.DisplayName = "n";
		local.Transport = McpTransportDto.Stdio;
		Assert.False(await local.TrySaveAsync());
		Assert.Equal(Constants.Settings.McpCommandRequired, local.ErrorText);
	}

	[Fact]
	public async Task Editor_TheDaemonsRefusal_IsShown_AndTheEditorStaysOpen()
	{
		var editor = NewEditor();
		editor.DisplayName = "n";
		editor.Url = "https://x.example.com";
		_client.McpException = new TessynRpcException(RemoteProtocol.ErrorCodes.InvalidParams, "A server at another origin needs the header value again.");

		Assert.False(await editor.TrySaveAsync());

		Assert.Contains("another origin", editor.ErrorText);
		Assert.False(editor.Saved);
		Assert.False(editor.IsSaving);
	}

	[Fact]
	public async Task Editor_OAuthEndpoints_AreSentAsAPatchOfWhatChanged_WithTheSecretOnlyWhenTyped()
	{
		var stored = Http("a", "OAuth", McpAuthTypeDto.Oauth2, McpServerStatusDto.NotConnected,
			oauth: new McpOAuthDto("https://auth.example.com/a", "https://auth.example.com/t", null, "client-1", HasClientSecret: true, "read", true));
		var editor = NewEditor(stored);
		Assert.Equal(string.Empty, editor.ClientSecretInput);
		Assert.Contains("stored", editor.ClientSecretText);

		editor.Scopes = "read write";
		var untouchedSecret = editor.BuildRequest();
		Assert.Equal("read write", untouchedSecret.Oauth?.Scopes);
		Assert.Null(untouchedSecret.Oauth?.ClientSecret);
		Assert.Null(untouchedSecret.Oauth?.TokenEndpoint);

		editor.ClientSecretInput = SecretClient;
		Assert.Equal(SecretClient, editor.BuildRequest().Oauth?.ClientSecret);
		await Task.CompletedTask;
	}

	// ---- discovery

	[Fact]
	public async Task Discovery_FillsTheEndpoints_ForAnOAuthServerWithAnAddress()
	{
		_client.McpDiscoverAnswer = new McpDiscoverResult(new McpDiscoveredConfigDto("https://a/auth", "https://a/token", "https://a/register", true), TimedOut: false);
		var editor = NewEditor();
		editor.AuthType = McpAuthTypeDto.Oauth2;
		editor.Url = "https://a.example.com/mcp";

		await editor.DiscoverAsync();

		Assert.Equal("https://a/auth", editor.AuthorizationEndpoint);
		Assert.Equal("https://a/token", editor.TokenEndpoint);
		Assert.Equal("https://a/register", editor.RegistrationEndpoint);
		Assert.Equal(Constants.Settings.McpDiscoveryFound, editor.DiscoveryText);
		Assert.False(editor.IsDiscovering);
	}

	[Fact]
	public async Task Discovery_IsSkipped_WhenItDoesNotApply()
	{
		var editor = NewEditor();
		editor.Url = "https://a.example.com/mcp";   // auth type None
		await editor.DiscoverAsync();

		editor.AuthType = McpAuthTypeDto.Oauth2;
		editor.Url = "not a url";
		await editor.DiscoverAsync();

		editor.Url = "https://a.example.com/mcp";
		editor.AuthorizationEndpoint = "https://known/auth";
		editor.TokenEndpoint = "https://known/token";   // both known: nothing to look for
		await editor.DiscoverAsync();

		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("mcp.discover", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Discovery_ATimeout_IsReported_AndKeepsWhatWasFound()
	{
		_client.McpDiscoverAnswer = new McpDiscoverResult(new McpDiscoveredConfigDto("https://a/auth", string.Empty, null, false), TimedOut: true);
		var editor = NewEditor();
		editor.AuthType = McpAuthTypeDto.Oauth2;
		editor.Url = "https://a.example.com/mcp";

		await editor.DiscoverAsync();

		Assert.Equal(Constants.Settings.McpDiscoveryTimedOut, editor.DiscoveryText);
		Assert.Equal("https://a/auth", editor.AuthorizationEndpoint);
		Assert.Equal(string.Empty, editor.TokenEndpoint);
	}

	[Fact]
	public async Task Discovery_CanBeCancelled_AndThenLeavesNoBusyState()
	{
		var started = new TaskCompletionSource();
		CancellationToken seen = default;
		_client.McpDiscoverBehavior = async token =>
		{
			seen = token;
			started.SetResult();
			await Task.Delay(Timeout.Infinite, token);
			return null!;
		};
		var editor = NewEditor();
		editor.AuthType = McpAuthTypeDto.Oauth2;
		editor.Url = "https://a.example.com/mcp";

		var running = editor.DiscoverAsync();
		await started.Task;
		Assert.True(editor.IsDiscovering);

		editor.CancelDiscoveryCommand.Execute(null);
		await running;

		Assert.True(seen.IsCancellationRequested);
		Assert.False(editor.IsDiscovering);
		Assert.Null(editor.DiscoveryText);
		Assert.Equal(string.Empty, editor.TokenEndpoint);
	}

	[Fact]
	public async Task Discovery_AFailure_IsShown_NotThrown()
	{
		_client.McpException = new TessynRpcException(RemoteProtocol.ErrorCodes.InvalidParams, "url must be http(s)");
		var editor = NewEditor();
		editor.AuthType = McpAuthTypeDto.Oauth2;
		editor.Url = "https://a.example.com/mcp";

		await editor.DiscoverAsync();

		Assert.Contains("http(s)", editor.DiscoveryText);
		Assert.False(editor.IsDiscovering);
	}

	[Fact]
	public async Task Discovery_ANewLookup_CancelsTheOneThatRuns()
	{
		var tokens = new List<CancellationToken>();
		var gate = new TaskCompletionSource<McpDiscoverResult>();
		_client.McpDiscoverBehavior = async token =>
		{
			tokens.Add(token);
			if (tokens.Count == 1)
			{
				await Task.Delay(Timeout.Infinite, token);
			}

			return await gate.Task;
		};
		var editor = NewEditor();
		editor.AuthType = McpAuthTypeDto.Oauth2;
		editor.Url = "https://a.example.com/mcp";

		var first = editor.DiscoverAsync();
		var second = editor.DiscoverAsync();
		gate.SetResult(new McpDiscoverResult(new McpDiscoveredConfigDto("https://n/auth", "https://n/token", null, true), false));
		await Task.WhenAll(first, second);

		Assert.True(tokens[0].IsCancellationRequested);
		Assert.Equal("https://n/token", editor.TokenEndpoint);
		Assert.False(editor.IsDiscovering);
	}

	// ---- Web access

	private WebAccessSectionViewModel NewWeb() => new(_client, NullLogger<WebAccessSectionViewModel>.Instance);

	[Fact]
	public async Task Web_ShowsTheStateAndWhetherATokenIsStored_NeverTheToken()
	{
		_client.RelayStatus = new RelayStatusDto(true, RelayStateDto.Connected, "https://tessyn.example.com", HasToken: true, DateTimeOffset.UtcNow);
		var web = NewWeb();

		await web.InitializeAsync();

		Assert.True(web.Enabled);
		Assert.Equal("https://tessyn.example.com", web.Url);
		Assert.Equal(Constants.Settings.RelayTokenSet, web.TokenText);
		Assert.Contains("Connected", web.StateText);
		Assert.Equal(string.Empty, web.LinkTokenInput);
	}

	[Fact]
	public async Task Web_SwitchingOn_NeedsAnAddressAndAToken_BeforeAnythingIsSent()
	{
		var web = NewWeb();
		await web.InitializeAsync();
		web.Enabled = true;
		await web.SaveAsync();
		Assert.Contains("URL", web.ErrorText);

		web.Url = "https://tessyn.example.com";
		await web.SaveAsync();
		Assert.Contains("token", web.ErrorText, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("relay.configure", _client.Calls);
	}

	[Fact]
	public async Task Web_Save_SendsTheChanges_TheTokenOnceAndForgetsIt()
	{
		var web = NewWeb();
		await web.InitializeAsync();
		web.Enabled = true;
		web.Url = " https://tessyn.example.com ";
		web.LinkTokenInput = "link-token-abc";

		await web.SaveAsync();

		Assert.Equal(["link-token-abc"], _client.RelayTokensReceived);
		Assert.Equal(string.Empty, web.LinkTokenInput);
		Assert.Equal(Constants.Settings.RelayTokenSet, web.TokenText);
		Assert.True(web.Enabled);
		Assert.Equal("https://tessyn.example.com", web.Url);
		Assert.DoesNotContain("link-token", web.StatusText + web.ErrorText + web.StateText + web.TokenText);
	}

	[Fact]
	public async Task Web_WithNothingChanged_SendsNothing_AndAKeptTokenIsNotResent()
	{
		_client.RelayStatus = new RelayStatusDto(true, RelayStateDto.Connected, "https://tessyn.example.com", HasToken: true, DateTimeOffset.UtcNow);
		var web = NewWeb();
		await web.InitializeAsync();

		await web.SaveAsync();

		Assert.Equal(Constants.Settings.NothingToSave, web.StatusText);
		Assert.DoesNotContain("relay.configure", _client.Calls);

		web.Enabled = false;   // switching off needs no token
		await web.SaveAsync();
		Assert.Empty(_client.RelayTokensReceived);
		Assert.Contains("relay.configure", _client.Calls);
	}

	[Fact]
	public async Task Web_ARefresh_DoesNotOverwriteWhatTheUserIsTyping()
	{
		var web = NewWeb();
		await web.InitializeAsync();
		web.Url = "https://other.example.com";
		web.Enabled = false;
		web.LinkTokenInput = "t";
		_client.RelayStatus = new RelayStatusDto(false, RelayStateDto.Disabled, "https://old.example.com", HasToken: true, DateTimeOffset.UnixEpoch);
		await web.RefreshAsync();   // a status from outside: the typed address survives a refresh
		Assert.Equal("https://other.example.com", web.Url);
	}

	[Fact]
	public async Task Web_ADaemonWithoutTheRelay_ShowsANote()
	{
		var client = new FakeTessynClient().AnnouncingSettings(RemoteProtocol.SettingsApi.Capability);
		var web = new WebAccessSectionViewModel(client, NullLogger<WebAccessSectionViewModel>.Instance);

		await web.InitializeAsync();

		Assert.False(web.IsAvailable);
		Assert.DoesNotContain("relay.status", client.Calls);
	}

	// ---- Models and profiles

	[Fact]
	public async Task Models_RefreshReportsTheCount()
	{
		_client.ModelsRefreshCount = 12;
		var profiles = new ProfileViewModel(new DialogService(_dialogs, new ImmediateUiDispatcher()), new ImmediateUiDispatcher(), NullLogger<ProfileViewModel>.Instance);
		var section = new ModelsProfilesSectionViewModel(_client, profiles, NullLogger<ModelsProfilesSectionViewModel>.Instance);

		await section.RefreshModelsAsync();

		Assert.Contains("12 model", section.StatusText);
		Assert.False(section.IsRefreshing);
	}

	[Fact]
	public async Task Models_ADaemonWithoutModelsAdmin_DoesNotCallRefresh()
	{
		var client = new FakeTessynClient().AnnouncingSettings(RemoteProtocol.SettingsApi.Capability);
		var profiles = new ProfileViewModel(new DialogService(_dialogs, new ImmediateUiDispatcher()), new ImmediateUiDispatcher(), NullLogger<ProfileViewModel>.Instance);
		var section = new ModelsProfilesSectionViewModel(client, profiles, NullLogger<ModelsProfilesSectionViewModel>.Instance);

		await section.RefreshModelsAsync();

		Assert.False(section.ModelsSupported);
		Assert.DoesNotContain("models.refresh", client.Calls);
	}
}
