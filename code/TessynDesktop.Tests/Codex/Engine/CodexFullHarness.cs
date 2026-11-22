using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;
using TessynDesktop.Tests.Codex.Fakes;
using TessynDesktop.Tests.Services;
using TessynDesktop.Tests.Services.Turns;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>
/// Both pipelines with every phase 3 piece in place: the model-to-engine selector, the ChatGPT profile resolver in the turn settings and the
/// headless service, the GPT catalog (loaded from the fake's default list on <c>codex_1</c>), usage, the profile service and the auth-error tracker.
/// The engine has its own clock for the idle unload and the Stop timeout.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class CodexFullHarness : IAsyncDisposable
{
	public CodexFullHarness()
	{
		H = new TurnEngineHarness(agentTools: true);
		S = new CodexServicesFixture(H.Settings, H.Files);
		Entries = new InMemoryEntryReader(H.Files);

		var config = new CodexThreadConfigBuilder(H.Settings, new Lazy<IAgentMcpServer>(() => Mcp), new McpConfigMerger(new FakeMcpOAuthService()));
		Codex  = new CodexSessionEngine(S.Pool, config, H.Files, H.Settings, registry: null, profiles: S.Resolver, time: EngineClock,
			models: S.Models, usage: S.Usage, profileService: S.Profiles, authErrors: H.AuthErrors);
		Claude = new ClaudeSessionEngine(H.Process, H.Streaming, H.Files, H.Settings, H.Status, new Lazy<IAgentMcpServer>(() => H.Mcp));
		Router = new SessionEngineRegistry([Claude, Codex], selector: S.Selector, appSettings: H.Settings);

		var claudeProfiles = new FakeProfileServiceForTurnService();
		var labels         = new ProfileLabelService(claudeProfiles, H.Settings);
		var resolver       = new ProfileResolver(H.Settings, claudeProfiles);
		var modelSettings  = new SessionModelSettings(H.Settings, S.Claude);
		Interactive = new TurnEngine(H.Files, H.Registry, H.Turns, Router, H.Settings, new Lazy<ITelegramBridgeService>(() => H.Telegram),
			H.Attention.Service, H.Tags.Service, [Progress], authErrors: H.AuthErrors, entries: Entries);
		TurnSettings = new TurnSettingsResolver(H.Settings, H.Turns, claudeProfiles, S.Claude, labels, resolver, modelSettings, Router, S.Resolver);
		Headless = new SessionTurnService(Router, H.Files, H.Settings, claudeProfiles, new FakeRegistryForTurnService(), S.Claude,
			new Lazy<ITelegramBridgeService>(() => H.Telegram), H.Attention.Service, H.Tags.Service, labels, [Progress],
			resolver, modelSettings: modelSettings, codexProfiles: S.Resolver);
	}

	public TurnEngineHarness H { get; }
	public InMemoryEntryReader Entries { get; }
	public CodexServicesFixture S { get; }
	public ManualTimerTimeProvider EngineClock { get; } = new();
	public FakeCmxMcpServer Mcp { get; } = new();
	public RecordingProgressSink Progress { get; } = new();
	public CodexSessionEngine Codex { get; }
	public ClaudeSessionEngine Claude { get; }
	public SessionEngineRegistry Router { get; }
	public TurnEngine Interactive { get; }
	public TurnSettingsResolver TurnSettings { get; }
	public SessionTurnService Headless { get; }

	/// <summary>Signs in codex_1 (and codex_2 when asked) and loads the GPT catalog.</summary>
	public async Task InitializeAsync(bool secondProfile = false)
	{
		S.AddStoredProfile("codex_1", "u@example.com");
		if (secondProfile)
			S.AddStoredProfile("codex_2", "w@example.com");
		await S.LoadCatalogAsync("codex_1");
	}

	/// <summary>A session in the harness's tree on <paramref name="model"/> (no engine stored yet: the turn decides it).</summary>
	public SessionNodeModel Node(string? model = "gpt-x-sol", string? effort = null, string? profileId = null)
	{
		var node = TurnEngineHarness.Node();
		node.ModelId   = model;
		node.Effort    = effort;
		node.ProfileId = profileId;
		H.Settings.Settings.Tree.Add(new DirectoryNodeModel { Path = node.WorkingDirectory, Sessions = [node] });
		return node;
	}

	/// <summary>An interactive turn the way a client sends it: the settings resolved for the session (and the per-turn picks), then the turn.</summary>
	public async Task<TurnOutcome> RunAsync(SessionNodeModel node, string message = "Hi", TurnOptionsModel? options = null, ITurnObserver? observer = null,
		TurnEngine? via = null)
	{
		var settings = await TurnSettings.ResolveAsync(node, options);
		return await (via ?? Interactive).RunAsync(new TurnRequest(node, message, settings, false), observer).WaitAsync(TimeSpan.FromSeconds(20));
	}

	/// <summary>Another interactive pipeline over the same pieces, with the given profile switch service (the one the daemon has).</summary>
	public TurnEngine NewInteractive(IProfileSwitchService profileSwitch) =>
		new(H.Files, H.Registry, H.Turns, Router, H.Settings, new Lazy<ITelegramBridgeService>(() => H.Telegram),
			H.Attention.Service, H.Tags.Service, [Progress], profileSwitch: profileSwitch, authErrors: H.AuthErrors, entries: Entries);

	public JsonElement[] Requests(string method) => S.Requests(method);

	public string? HomeOf(string profileId) => S.Profiles.Find(profileId)!.CodexHome;

	public async ValueTask DisposeAsync()
	{
		await S.DisposeAsync();
		H.Dispose();
	}
}
