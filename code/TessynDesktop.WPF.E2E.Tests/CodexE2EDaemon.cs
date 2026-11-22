using TessynDesktop.Codex;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Codex.Engine;
using TessynDesktop.Tests.Codex.Fakes;
using TessynDesktop.Tests.Contract;
using TessynDesktop.Tests.Services;
using TessynDesktop.Tests.Services.Turns;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// The test daemon of the FR.33 WPF E2E tests (FR.33.19, TC.CX.40, TC.CX.41): a real loopback <see cref="TdcpServer"/> whose GPT side is the real
/// Codex services and engine over the <b>in-process fake app-server</b> the daemon tests use (<see cref="CodexServicesFixture"/>), the same way the other
/// E2E tests use a fake <c>claude</c>. Real modules: hello (with the <c>engines</c> and <c>profiles.codex</c> capabilities), the session, attention and
/// file modules of <see cref="FocusRefsEnv"/>, the tree, <c>catalog.get</c> (GPT models from the fake's <c>model/list</c>), <c>usage.get</c> (the
/// fake's rate limits), <c>settings.*</c> with the Codex members, profile administration with ChatGPT sign-in, and <c>turn.*</c> through a real
/// <see cref="TurnEngine"/> whose router has the Codex engine. Codex is "found" at <see cref="CodexExe"/> by a fake locator answering
/// <c>codex-cli 0.171.0</c>. Fixture C (TC.CX.01): ChatGPT <c>codex_1</c> (<c>u@example.com</c>, plus) is signed in and is the catalog profile.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class CodexE2EDaemon : IAsyncDisposable
{
	public const string CodexExe = @"C:\Tools\Codex\codex.exe";
	public const string ChatGptProfile = "codex_1";
	public const string ChatGptName = "ChatGPT · u@example.com";

	private readonly ClientTestHost _host;
	private readonly TurnEventPublisher _publisher;

	private CodexE2EDaemon(bool toolTurns)
	{
		Env.WriteBodies("question 0", "answer 1");

		Codex = new CodexServicesFixture(Settings);
		if (toolTurns)
			Codex.Launcher.Configure = server => server.TurnScript = t => server.Scripts.ToolTurn(t.ThreadId, t.TurnId);
		LocatorEnvironment.Files.Add(CodexExe);
		LocatorEnvironment.VersionOutputs[CodexExe] = "codex-cli 0.171.0";
		Settings.Settings.CodexPath = CodexExe;

		ClaudeProfiles = new ScriptedProfileService(Path.Combine(Codex.Root, "profiles"));
		ClaudeProfiles.SetEmail(null, "system.account@example.com");
		var labels = new ProfileLabelService(ClaudeProfiles, Settings);

		Sections     = new SettingsSectionService(Settings, [new GeneralSettingsSection(new CodexSettingsService(Settings, new CodexExecutableLocator(LocatorEnvironment)))], Bus);
		var profileAdmin = new ProfileAdminService(ClaudeProfiles, Settings, Bus);
		var codexAdmin   = new CodexProfileAdminService(Codex.Profiles, Settings, Bus);
		var usage        = new RemoteUsageService(new StubUsageFetcher(), new StubDesktopUsage(), ClaudeProfiles, Settings, labels, Bus, Codex.Clock, engineSources: [Codex.Usage]);

		// The turn pipeline: the Claude engine is the light one (these tests send GPT turns only), the Codex engine drives the fake app-server.
		var config      = new CodexThreadConfigBuilder(Settings, new Lazy<IAgentMcpServer>(() => Mcp), new McpConfigMerger(new FakeMcpOAuthService()));
		var codexEngine = new CodexSessionEngine(Codex.Pool, config, Env.Base.Files, Settings, profiles: Codex.Resolver, models: Codex.Models, usage: Codex.Usage,
			profileService: Codex.Profiles);
		var claudeEngine = new ClaudeSessionEngine(null!, new StubStreamingManager(), null!, Settings, null!, new Lazy<IAgentMcpServer>(() => null!));
		Router = new SessionEngineRegistry([claudeEngine, codexEngine], selector: Codex.Selector, appSettings: Settings);
		_publisher = new TurnEventPublisher(Bus, TimeSpan.FromMilliseconds(20));
		var turns   = new SharedLockTurnService();
		Engine      = new TurnEngine(Env.Base.Files, Env.Base.Registry, turns, Router, Settings, new Lazy<ITelegramBridgeService>(() => new RecordingTelegramBridge()),
			Env.Attention, Env.Base.TagService, [_publisher]);
		var modelSettings = new SessionModelSettings(Settings, Codex.Claude, Codex.Models);
		var options  = new TurnOptionsResolver(Codex.Claude, Settings, labels, Codex.Models, Codex.Resolver);
		var resolver = new TurnSettingsResolver(Settings, turns, ClaudeProfiles, Codex.Claude, labels, new ProfileResolver(Settings, ClaudeProfiles), modelSettings, Router, Codex.Resolver);
		var turnModule = new TurnRpcModule(Env.Base.Registry, options, resolver, new TurnSendService(Engine, Env.Base.Registry), Engine, turns, Router, modelSettings);
		Env.Base.Files.TurnCompleted += (_, e) =>
		{
			var node = Env.Base.Registry.FindModelByFileName(e.FileName);
			if (node != null)
				Bus.Publish(RemoteProtocol.Events.TurnCompleted, new TurnCompletedPayload(node.NodeId, e.Cancelled, e.ErrorText, e.TurnId));
		};

		var activity = new ActivityByNodeService();
		var states   = new SessionStateBuilder(Env.Base.Registry, Router, Env.Attention, activity, codexProfiles: Codex.Profiles);
		var tree     = new TreeRemoteRpcModule(new RemoteTreeReader(Settings), Env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());

		ClientTestHost? host = null;
		Bus.Published += n => host?.Bus.Publish(n.Method, n.Params);   // the services publish on this bus; the stream reads the host's
		_host = host = new ClientTestHost((run, log) => new TdcpDispatcher(
		[
			new ClientRpcModule(runInfo: run, eventLog: log, turnEngine: Engine, settingsSections: Sections, profileAdmin: profileAdmin, engines: Engines,
				codexProfiles: Codex.Profiles),
			.. Env.Modules,
			tree,
			new SettingsRpcModule(Sections),
			new ProfileAdminRpcModule(profileAdmin, labels, Codex.Profiles, codexAdmin, Settings),
			new CatalogRpcModule(Codex.Claude, Settings, labels, Codex.Models, Codex.Profiles, Engines),
			new UsageRpcModule(usage),
			turnModule,
		]));
	}

	public FocusRefsEnv Env { get; } = new();
	public AppSettingsService Settings => Env.Base.Settings;
	public CodexServicesFixture Codex { get; }
	public FakeCodexLocatorEnvironment LocatorEnvironment { get; } = new();
	public ScriptedProfileService ClaudeProfiles { get; }
	public RemoteEventBus Bus { get; } = new();
	public SettingsSectionService Sections { get; }
	public SessionEngineRegistry Router { get; }
	public TurnEngine Engine { get; }
	public FakeCmxMcpServer Mcp { get; } = new();
	public FixedEngineAvailability Engines { get; } = new() { Available = [Constants.SessionEngines.Claude, Constants.SessionEngines.Codex] };
	public ClientTestHost Host => _host;
	public SessionNodeModel Session => Env.Session;

	/// <summary>Builds the daemon, signs <see cref="ChatGptProfile"/> in and loads the GPT catalog from the fake app-server.</summary>
	/// <param name="toolTurns">Every GPT turn plays the fake's tool turn (commentary, an MCP call, commands, a file change, a plan, then the answer).</param>
	public static async Task<CodexE2EDaemon> StartAsync(bool toolTurns = false)
	{
		var daemon = new CodexE2EDaemon(toolTurns);
		await daemon.Codex.LoadCatalogAsync(ChatGptProfile);
		return daemon;
	}

	/// <summary>Writes the token and the discovery file the app finds the daemon by, and the client state that opens <see cref="Session"/>.</summary>
	public string WriteDiscovery(string root)
	{
		var tokens = new TdcpTokenStore(Path.Combine(root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		var daemonFile = Path.Combine(root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, daemonFile).Write(_host.Port);

		var clients = Directory.CreateDirectory(Path.Combine(root, "clients"));
		File.WriteAllText(Path.Combine(clients.FullName, "wpf.json"),
			$"{{\"lastSelectedNodeId\":\"{Session.NodeId}\",\"expandedNodeIds\":[\"{Env.Base.Directory.NodeId}\"]}}");
		return daemonFile;
	}

	/// <summary>The fake app-server of a stored ChatGPT profile (the latest one launched for its home).</summary>
	public FakeCodexAppServer ServerOf(string profileId) => Codex.ServerOf(Path.Combine(Codex.StoredRoot, profileId));

	/// <summary>Codex reports the end of a sign-in on the profile's host (the browser or the code was used).</summary>
	public Task CompleteLoginAsync(string profileId, string loginId, bool success, string? error = null) =>
		ServerOf(profileId).NotifyAsync("account/login/completed", new System.Text.Json.Nodes.JsonObject { ["loginId"] = loginId, ["success"] = success, ["error"] = error });

	public async ValueTask DisposeAsync()
	{
		_host.Dispose();
		_publisher.Dispose();
		Sections.Dispose();
		Bus.Dispose();
		await Codex.DisposeAsync();
		Env.Dispose();
	}
}
