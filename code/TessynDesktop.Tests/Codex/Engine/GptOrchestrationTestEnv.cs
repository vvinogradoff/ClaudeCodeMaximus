using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>
/// An <see cref="AgentMcpServer"/> with the phase 3 GPT pieces (FR.33.15): the merged catalog with the fake's GPT list, the ChatGPT profiles
/// <c>codex_1</c> (u@example.com) and <c>codex_2</c> (w@example.com), the GPT tier table, and the Claude profiles p1 "Account A" and p2
/// "Account B"; a gated turn service so no turn runs. One project directory <see cref="Dir"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class GptOrchestrationTestEnv : IAsyncDisposable
{
	private GptOrchestrationTestEnv(string root)
	{
		Root     = root;
		Settings = new AppSettingsService(root);
		Registry = new SessionRegistry(Settings, Files, new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
		S        = new CodexServicesFixture(Settings);
	}

	public string Root { get; }
	public string ProfilesRoot => Path.Combine(Root, "profiles");
	public AppSettingsService Settings { get; }
	public FakeSessionFileService Files { get; } = new();
	public SessionRegistry Registry { get; }
	public GatedTurnService Turns { get; } = new();
	public CodexServicesFixture S { get; }
	public AgentMcpServer Server { get; private set; } = null!;
	public DirectoryNodeModel Dir { get; private set; } = null!;

	public static async Task<GptOrchestrationTestEnv> CreateAsync(bool signedIn = true)
	{
		var env = new GptOrchestrationTestEnv(Path.Combine(Path.GetTempPath(), "cm_gpt_orch_" + Path.GetRandomFileName()));
		Directory.CreateDirectory(env.ProfilesRoot);
		env.AddClaudeProfile("p1", "Account A");
		env.AddClaudeProfile("p2", "Account B");

		env.S.AddStoredProfile("codex_1", "u@example.com");
		env.S.AddStoredProfile("codex_2", "w@example.com");
		await env.S.LoadCatalogAsync("codex_1");
		if (!signedIn)
		{
			env.S.Profiles.SetAuthError("codex_1", true);
			env.S.Profiles.SetAuthError("codex_2", true);
		}

		env.Server = new AgentMcpServer(
			env.Settings,
			new Lazy<ISessionTurnService>(() => env.Turns),
			new Lazy<ISchedulerService>(() => new RecordingScheduler()),
			env.Registry, env.Files,
			env.S.Claude, tdcpServer: null!, configMerger: null!,
			new FakeTelegramBridge(), new FakeNotificationService(), new FakeDirectoryLabelService(),
			new AttentionService(new AttentionLedgerStore(Path.Combine(env.Root, "attention")), env.Files),
			TagTestEnv.Unused(),
			profiles: new ProfileResolver(env.Settings, new TempProfileService(env.ProfilesRoot)),
			clock: new ManualTimeProvider(),
			sessionModels: env.S.Models,
			codexProfiles: env.S.Resolver,
			tiers: env.S.Tiers);

		env.Dir = env.Registry.AddDirectory(Path.Combine(env.Root, "dir"));
		return env;
	}

	public void AddClaudeProfile(string id, string name)
	{
		Settings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = id, DisplayName = name });
		var dir = Path.Combine(ProfilesRoot, id);
		Directory.CreateDirectory(dir);
		File.WriteAllText(ClaudeCredentialsPaths.Resolve(dir), "{}");
	}

	/// <summary>A top-level Claude supervisor in <see cref="Dir"/>, pinned to p2, on <paramref name="model"/>.</summary>
	public SessionNodeModel AddClaudeSupervisor(string name = "Sup", string model = FixedClaudeModels.Opus)
	{
		var node = Registry.AddSession(Dir, name);
		node.ProfileId   = "p2";
		node.ProfileName = "Account B";
		node.ModelId     = model;
		return node;
	}

	/// <summary>A top-level GPT session in <see cref="Dir"/> on <paramref name="profileId"/>.</summary>
	public SessionNodeModel AddGptSession(string name = "G", string profileId = "codex_1", string model = "gpt-x-sol")
	{
		var node = Registry.AddSession(Dir, name);
		node.EngineId    = Constants.SessionEngines.Codex;
		node.ProfileId   = profileId;
		node.ProfileName = S.Profiles.DisplayNameOf(profileId);
		node.ModelId     = model;
		node.CodexThreadId = "01a20229-0000-7000-8000-00000000abcd";
		return node;
	}

	public Task<JsonNode> Call(string tool, JsonObject arguments, SessionNodeModel caller) =>
		Server.HandleToolCallAsync(new JsonObject { ["name"] = tool, ["arguments"] = arguments }, caller);

	public static string TextOf(JsonNode result) => result["content"]![0]!["text"]!.GetValue<string>();

	public SessionNodeModel Named(string name) =>
		Settings.Settings.Tree.SelectMany(d => d.Sessions.Concat(d.Groups.SelectMany(g => g.Sessions))).Single(s => s.Name == name);

	public async ValueTask DisposeAsync()
	{
		await S.DisposeAsync();
		try
		{
			Directory.Delete(Root, recursive: true);
		}
		catch (IOException)
		{
		}
	}
}
