using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// An <see cref="AgentMcpServer"/> over a real registry, settings and profile resolver in temp directories, with a model catalog (Opus, Sonnet,
/// Haiku), stored profiles p1 "Account A" and p2 "Account B", a gated turn service and a manual clock, for <c>create_session</c> (FR.15.15).
/// Tree: directory <see cref="Dir"/> with group <see cref="Ui"/> holding <see cref="First"/>, <see cref="Anchor"/>, <see cref="Last"/>;
/// directory <see cref="Dir2"/> is empty.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class CreateSessionTestEnv : IDisposable
{
	public const string Opus   = "claude-opus-5-5";
	public const string Sonnet = "claude-sonnet-5-5";
	public const string Haiku  = "claude-haiku-4-5-20251001";

	public string Root { get; } = Path.Combine(Path.GetTempPath(), "cm_create_session_" + Path.GetRandomFileName());
	public string ProfilesRoot => Path.Combine(Root, "profiles");

	public AppSettingsService Settings { get; }
	public FakeSessionFileService Files { get; } = new();
	public SessionRegistry Registry { get; }
	public ScriptedModelService Models { get; } = new();
	public GatedTurnService Turns { get; } = new();
	public RecordingScheduler Scheduler { get; } = new();
	public ManualTimeProvider Clock { get; } = new();
	public AgentMcpServer Server { get; }
	public List<TreeChangedEventArgs> Added { get; } = [];

	public DirectoryNodeModel Dir { get; }
	public DirectoryNodeModel Dir2 { get; }
	public GroupNodeModel Ui { get; }
	public SessionNodeModel First { get; }
	public SessionNodeModel Anchor { get; }
	public SessionNodeModel Last { get; }

	public CreateSessionTestEnv()
	{
		Directory.CreateDirectory(ProfilesRoot);
		Settings = new AppSettingsService(Root);
		Registry = new SessionRegistry(Settings, Files, new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
		Models.Models = [new ClaudeModelInfo(Opus, "opus", "Opus 5.5"), new ClaudeModelInfo(Sonnet, "sonnet", "Sonnet 5.5"), new ClaudeModelInfo(Haiku, "haiku", "Haiku 4.5")];
		AddProfile("p1", "Account A");
		AddProfile("p2", "Account B");

		var profileService = new TempProfileService(ProfilesRoot);
		Server = new AgentMcpServer(
			Settings,
			new Lazy<ISessionTurnService>(() => Turns),
			new Lazy<ISchedulerService>(() => Scheduler),
			Registry, Files,
			Models, tdcpServer: null!, configMerger: null!,
			new FakeTelegramBridge(), new FakeNotificationService(), new FakeDirectoryLabelService(),
			new AttentionService(new AttentionLedgerStore(Path.Combine(Root, "attention")), Files),
			TagTestEnv.Unused(),
			profiles: new ProfileResolver(Settings, profileService),
			clock: Clock);

		Dir    = Registry.AddDirectory(Path.Combine(Root, "dir"));
		Dir2   = Registry.AddDirectory(Path.Combine(Root, "dir2"));
		Ui     = Registry.AddGroup(Dir, "UI");
		First  = Registry.AddSessionToGroup(Ui, "First");
		Anchor = Registry.AddSessionToGroup(Ui, "Anchor");
		Last   = Registry.AddSessionToGroup(Ui, "Last");

		Registry.TreeChanged += (_, e) =>
		{
			if (e.Kind == TreeChangeKind.SessionAdded)
				lock (Added) Added.Add(e);
		};
	}

	public void Dispose()
	{
		try { Directory.Delete(Root, recursive: true); }
		catch (IOException) { }
	}

	/// <summary>Adds a stored profile whose config dir exists; <paramref name="loggedIn"/> writes its credentials file.</summary>
	public void AddProfile(string id, string name, bool loggedIn = true)
	{
		Settings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = id, DisplayName = name });
		var dir = Path.Combine(ProfilesRoot, id);
		Directory.CreateDirectory(dir);
		if (loggedIn)
			File.WriteAllText(ClaudeCredentialsPaths.Resolve(dir), "{}");
	}

	/// <summary>A top-level caller directly in <see cref="Dir"/>, pinned to profile p2, on Opus (so every tier is allowed unless a test says otherwise).</summary>
	public SessionNodeModel AddCaller(string name = "Caller", string? modelId = Opus)
	{
		var caller = Registry.AddSession(Dir, name);
		caller.ProfileId   = "p2";
		caller.ProfileName = "Account B";
		caller.ModelId     = modelId;
		return caller;
	}

	public Task<JsonNode> Create(JsonObject arguments, SessionNodeModel caller) =>
		Server.HandleToolCallAsync(new JsonObject { ["name"] = "create_session", ["arguments"] = arguments }, caller);

	/// <summary>Every session in the tree, workers included.</summary>
	public IReadOnlyList<SessionNodeModel> AllSessions()
	{
		var all = new List<SessionNodeModel>();
		void Walk(List<SessionNodeModel> sessions, List<GroupNodeModel> groups)
		{
			all.AddRange(sessions);
			foreach (var group in groups)
				Walk(group.Sessions, group.Groups);
		}

		foreach (var dir in Settings.Settings.Tree)
			Walk(dir.Sessions, dir.Groups);
		return all;
	}

	public SessionNodeModel Named(string name) => AllSessions().Single(s => s.Name == name);
}
