using System;
using System.Collections.Generic;
using System.IO;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// Package J3 over a real loopback <see cref="TdcpServer"/>: the real settings service, profile administration service and the three RPC modules on
/// a temp data root, with a scripted profile service (no process) and a recording sound service. The settings hold seeded secrets so that a test can
/// prove none of them reaches a result. Disposing deletes the temp directory.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class SettingsAdminEnv : IDisposable
{
	/// <summary>Values that live in the settings file (or beside it) and must never appear in a result.</summary>
	public static readonly string[] Secrets =
	[
		"tdcp-legacy-secret-0001", "hunter2-static-header-value", "oauth-client-secret-9f8e7d",
		"123456789:AAHsecretTelegramLookingTokenValue_abcdef", // parked in a topic name, which no section exposes
		"q-secret-key-777",                                    // the query of an MCP server URL
		"stdio-arg-secret-555",                                // an argument of an MCP stdio command
		ScriptedMcpOAuth.AccessToken, ScriptedMcpOAuth.RefreshToken, TelegramBotToken, RelayLinkToken,
	];

	/// <summary>The stored Telegram bot token and relay link token: write-only, so no result may carry either.</summary>
	public const string TelegramBotToken = "987654321:AAFsecretBotTokenForTestsOnly_0123456789ab";
	public const string RelayLinkToken = "relay-link-token-secret-xyz-0001";

	/// <summary>The account of the Default profile: an e-mail may appear in <c>profile.list</c> only.</summary>
	public const string DefaultAccountEmail = "system.account@example.com";

	private readonly string _dir;

	public AppSettingsService Settings { get; }
	public ScriptedProfileService ProfileService { get; }
	public RecordingSoundService Sound { get; } = new();
	public RemoteEventBus Bus { get; } = new();
	public SettingsSectionService Sections { get; }
	public ProfileAdminService ProfileAdmin { get; }
	public InMemoryMcpTokenStore McpTokens { get; } = new();
	public ScriptedMcpOAuth McpOAuth { get; }
	public McpRegistryService Mcp { get; }
	public FakeTelegramTokenStore TelegramTokens { get; } = new() { Token = TelegramBotToken };
	public IntegrationTelegramBridge TelegramBridge { get; }
	public RelayConfigStore RelayStore { get; }
	public RecordingRelayConnector Relay { get; } = new();
	public ScriptedModelService ModelCatalog { get; } = new();
	public TdcpDispatcher Dispatcher { get; }
	public TdcpTestHost Host { get; }
	public string DataRoot => _dir;

	public SettingsAdminEnv()
	{
		_dir = Path.Combine(Path.GetTempPath(), "j3-" + Guid.NewGuid().ToString("N")[..8]);
		Directory.CreateDirectory(_dir);

		Settings = new AppSettingsService(_dir);
		Settings.Settings.SessionFilesRoot = Path.Combine(_dir, "sessions");
		Settings.Settings.ClaudePath       = "claude-test";
		Settings.Settings.TdcpToken        = Secrets[0];
		Settings.Settings.ExternalMcpServers.Add(new Models.ExternalMcpServerModel
		{
			Id = "mcp1", DisplayName = "Docs", Url = "https://docs.example.com/mcp?key=" + Secrets[4], AuthType = Models.McpServerAuthType.StaticHeader,
			StaticHeaderName = "Authorization", StaticHeaderValue = Secrets[1], OAuth = new Models.McpOAuthConfig { ClientSecret = Secrets[2] },
		});
		Settings.Settings.ExternalMcpServers.Add(new Models.ExternalMcpServerModel
		{
			Id = "mcp2", DisplayName = "Tracker", Url = "https://tracker.example.com/mcp", AuthType = Models.McpServerAuthType.OAuth2,
			OAuth = new Models.McpOAuthConfig
			{
				AuthorizationEndpoint = "https://auth.example.com/authorize", TokenEndpoint = "https://auth.example.com/token", ClientId = "client-123",
				ClientSecret = Secrets[2], Scopes = "read write", DiscoveryCompleted = true, ConnectedIdentity = "Alex Example",
			},
		});
		Settings.Settings.ExternalMcpServers.Add(new Models.ExternalMcpServerModel
		{
			Id = "mcp3", DisplayName = "Local", Transport = Models.McpServerTransport.Stdio, Command = "roslyn-mcp", Arguments = "--stdio --api-key=" + Secrets[5],
			WorkingDirectory = Path.Combine(_dir, "work"), Enabled = false,
		});
		McpTokens.Save("mcp2", new Models.McpTokenRecord { AccessToken = ScriptedMcpOAuth.AccessToken, RefreshToken = ScriptedMcpOAuth.RefreshToken });
		Settings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;
		Settings.Settings.TelegramTopicNamesByNodeId["node-1"] = Secrets[3];
		Settings.Settings.SessionCompletionSoundFile = Path.Combine(_dir, "ding.wav");

		ProfileService = new ScriptedProfileService(Path.Combine(_dir, "profiles"));
		ProfileService.SetEmail(null, DefaultAccountEmail);
		var labels = new ProfileLabelService(ProfileService, Settings);

		RelayStore = new RelayConfigStore(Path.Combine(_dir, "web-relay.dat"));
		RelayStore.Save(new Models.RelayConfigModel { Enabled = true, Url = "https://web.example.com", Token = RelayLinkToken });
		Relay.Status = new Models.RelayStatusModel(Models.RelayState.Connected, new DateTimeOffset(2026, 11, 15, 8, 30, 0, TimeSpan.Zero));
		McpOAuth       = new ScriptedMcpOAuth(McpTokens);
		Mcp            = new McpRegistryService(Settings, McpTokens, McpOAuth, Bus, discoverTimeout: TimeSpan.FromMilliseconds(400));
		TelegramBridge = new IntegrationTelegramBridge(Settings);

		Sections     = new SettingsSectionService(Settings, [new GeneralSettingsSection(), new NotificationsSettingsSection(), new TelegramSettingsSection()], Bus);
		ProfileAdmin = new ProfileAdminService(ProfileService, Settings, Bus);

		var runInfo  = new DaemonRunInfo();
		var helloLog = new EventLog(Bus);
		Dispatcher = new TdcpDispatcher(
		[
			new ClientRpcModule(runInfo: runInfo, eventLog: helloLog, settingsSections: Sections, profileAdmin: ProfileAdmin, sounds: Sound,
				mcpRegistry: Mcp, telegramTokens: TelegramTokens, relayConfig: RelayStore, models: ModelCatalog),
			new SettingsRpcModule(Sections),
			new ProfileAdminRpcModule(ProfileAdmin, labels),
			new SoundRpcModule(Sound),
			new McpRegistryRpcModule(Mcp),
			new TelegramSettingsRpcModule(TelegramTokens, TelegramBridge, Settings),
			new RelaySettingsRpcModule(RelayStore, new Lazy<IRelayConnectorService>(Relay)),
			new ModelsRpcModule(ModelCatalog, Bus, TimeSpan.FromMilliseconds(400)),
		]);
		Host = new TdcpTestHost(Dispatcher, bus: Bus, runInfo: runInfo);
	}

	public void AddStoredProfile(string id, string name, bool loggedIn = true)
	{
		Settings.Settings.Profiles.Add(new Models.ClaudeProfileModel { ProfileId = id, DisplayName = name });
		if (loggedIn)
		{
			var dir = Path.Combine(ProfileService.ProfilesRootDirectory, id);
			Directory.CreateDirectory(dir);
			File.WriteAllText(ClaudeCredentialsPaths.Resolve(dir), "{}");
		}
	}

	/// <summary>The names of every method the J3 modules register, with whether the relay may call it.</summary>
	public IReadOnlyList<(string Name, bool RemoteCallable)> Methods()
	{
		var registry = new TdcpMethodRegistry();
		new SettingsRpcModule(Sections).Register(registry);
		new ProfileAdminRpcModule(ProfileAdmin, new ProfileLabelService(ProfileService, Settings)).Register(registry);
		new SoundRpcModule(Sound).Register(registry);
		new McpRegistryRpcModule(Mcp).Register(registry);
		new TelegramSettingsRpcModule(TelegramTokens, TelegramBridge, Settings).Register(registry);
		new RelaySettingsRpcModule(RelayStore, new Lazy<IRelayConnectorService>(Relay)).Register(registry);
		new ModelsRpcModule(ModelCatalog, Bus).Register(registry);
		return [.. registry.Entries.Select(e => (e.Name, e.RemoteCallable))];
	}

	public void Dispose()
	{
		Host.Dispose();
		Sections.Dispose();
		Bus.Dispose();
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, recursive: true);
	}
}
