using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>Profile service over a temp profiles root: profile N's config dir is <c>&lt;root&gt;\&lt;ProfileId&gt;</c>.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class TempProfileService(string profilesRoot) : IClaudeProfileService
{
	public string ProfilesRootDirectory => profilesRoot;
	public Task<string?> GetAccountEmailAsync(string claudePath, string? configDir) => Task.FromResult<string?>(null);
	public Task LaunchAuthLoginAsync(string claudePath, string? configDir) => throw new NotImplementedException();

	public string? GetConfigDirForProfile(int profileIndex, IReadOnlyList<ClaudeProfileModel> profiles) =>
		profileIndex >= 1 && profileIndex <= profiles.Count ? Path.Combine(profilesRoot, profiles[profileIndex - 1].ProfileId) : null;

	public void MarkProfileDirectoryRemoved(string profileId) => throw new NotImplementedException();
}

/// <summary>Streaming manager that only knows which nodes have a live process and which were stopped.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class RecordingStreamingManager : IClaudeStreamingSessionManager
{
	public HashSet<string> Alive { get; } = [];
	public List<string> Stopped { get; } = [];

	public bool EnsureStarted(string nodeId, StreamingSpawnParams spawnParams, Action<ClaudeStreamEvent> onEvent) => throw new NotImplementedException();
	public Task SendTurnAsync(string nodeId, string userMessage, CancellationToken cancellationToken, Action<ClaudeStreamEvent>? onTurnEvent = null) => Task.CompletedTask;
	public bool IsAlive(string nodeId) => Alive.Contains(nodeId);

	public void Stop(string nodeId)
	{
		Stopped.Add(nodeId);
		Alive.Remove(nodeId);
	}

	public void TerminateAll() { }
	public event Action<string>? ProcessExited { add { } remove { } }
}

/// <summary>Turn service that records runs and resolves the profile name like the real one (the node's pin; no file history).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ProfileEnvTurnService : ISessionTurnService
{
	private readonly SemaphoreSlim _lock = new(1, 1);
	public List<string> RunNodeIds { get; } = [];

	public SemaphoreSlim GetTurnLock(string nodeId) => _lock;

	public Task<TurnResultModel> RunTurnAsync(SessionNodeModel node, string prompt, TurnSource source, CancellationToken cancellationToken = default)
	{
		RunNodeIds.Add(node.NodeId);
		return Task.FromResult(new TurnResultModel(AssistantText: "ok", SessionId: null));
	}

	public Task<TurnResultModel> RunTurnAsync(SessionNodeModel node, string prompt, TurnSource source, TurnOptionsModel? options, CancellationToken cancellationToken = default) =>
		RunTurnAsync(node, prompt, source, cancellationToken);

	public bool CancelTurn(string nodeId) => false;
	public string? ResolveProfileName(SessionNodeModel node) => node.ProfileName;
}

/// <summary>
/// A real registry, settings, resolver, migrator and switch service over temp directories (ISSUE.013). Profiles are fake
/// folders under a temp profiles root; the Default profile's home is a temp folder too, so no real profile data is touched.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ProfileSwitchEnv : IDisposable
{
	public string Root { get; } = Path.Combine(Path.GetTempPath(), "cm_i013_" + Path.GetRandomFileName());
	public string ProfilesRoot => Path.Combine(Root, "profiles");
	public string DefaultHome => Path.Combine(Root, "default-claude-home");

	public AppSettingsService Settings { get; }
	public FakeSessionFileService Files { get; } = new();
	public SessionRegistry Registry { get; }
	public RecordingStreamingManager Streaming { get; } = new();
	public TempProfileService ProfileService { get; }
	public ProfileResolver Resolver { get; }
	public ClaudeSessionMigrator Migrator { get; }
	public ProfileSwitchService Switch { get; }
	public ProfileEnvTurnService Turns { get; } = new();
	public AgentMcpServer Server { get; }
	public DirectoryNodeModel Dir { get; }

	public ProfileSwitchEnv()
	{
		Directory.CreateDirectory(ProfilesRoot);
		Directory.CreateDirectory(DefaultHome);
		Settings       = new AppSettingsService(Root);
		Registry       = new SessionRegistry(Settings, Files, new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
		ProfileService = new TempProfileService(ProfilesRoot);
		Resolver       = new ProfileResolver(Settings, ProfileService);
		Migrator       = new ClaudeSessionMigrator(ProfileService, DefaultHome);
		Switch         = new ProfileSwitchService(Settings, Registry, Files, Resolver, Migrator, Streaming, new Lazy<ISessionTurnService>(() => Turns));
		Server = new AgentMcpServer(
			Settings,
			new Lazy<ISessionTurnService>(() => Turns),
			new Lazy<ISchedulerService>(() => new RecordingScheduler()),
			Registry, Files,
			modelService: null!, tdcpServer: null!, configMerger: null!,
			new FakeTelegramBridge(), new FakeNotificationService(), new FakeDirectoryLabelService(),
			new AttentionService(new AttentionLedgerStore(Path.Combine(Root, "attention")), Files),
			TagTestEnv.Unused(),
			profiles: Resolver, profileSwitch: Switch);
		Dir = Registry.AddDirectory(Path.Combine(Root, "dir"));
	}

	public void Dispose()
	{
		try { Directory.Delete(Root, recursive: true); }
		catch (IOException) { }
	}

	/// <summary>Adds a stored profile whose config dir exists; <paramref name="loggedIn"/> writes its credentials file.</summary>
	public string AddProfile(string id, string name, bool loggedIn = true)
	{
		Settings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = id, DisplayName = name });
		var dir = Path.Combine(ProfilesRoot, id);
		Directory.CreateDirectory(dir);
		if (loggedIn)
			File.WriteAllText(ClaudeCredentialsPaths.Resolve(dir), "{}");
		return dir;
	}

	/// <summary>Adds a session in <see cref="Dir"/> with a Claude session id, pinned to <paramref name="profileId"/> when given.</summary>
	public SessionNodeModel AddSession(string name, SessionNodeModel? supervisor = null, string? profileId = null)
	{
		var session = Registry.AddSession(Dir, name);
		session.ClaudeSessionId = Guid.NewGuid().ToString();
		Files.LastWriteTimes[session.FileName] = DateTimeOffset.UtcNow.AddMinutes(-5);
		if (supervisor != null)
		{
			session.SupervisorNodeId   = supervisor.NodeId;
			session.OrchestrationDepth = supervisor.OrchestrationDepth + 1;
		}
		if (profileId != null)
		{
			session.ProfileId   = profileId;
			session.ProfileName = Settings.Settings.Profiles.FirstOrDefault(p => p.ProfileId == profileId)?.DisplayName;
		}
		return session;
	}

	/// <summary>The slug folder of the session's project inside a config dir (<c>null</c> = the Default home).</summary>
	public string SlugDir(string? configDir, SessionNodeModel session) =>
		Path.Combine(configDir ?? DefaultHome, Constants.ClaudeSessions.ProjectsFolderName, Constants.ClaudeSessions.BuildProjectSlug(session.WorkingDirectory));

	public string TranscriptIn(string? configDir, SessionNodeModel session) =>
		Path.Combine(SlugDir(configDir, session), session.ClaudeSessionId + Constants.ClaudeSessions.SessionFileExtension);

	public string FolderIn(string? configDir, SessionNodeModel session) => Path.Combine(SlugDir(configDir, session), session.ClaudeSessionId!);

	/// <summary>Writes the session's transcript and one tool-result file into a config dir.</summary>
	public void WriteSession(string? configDir, SessionNodeModel session, string transcript = "{\"type\":\"user\"}\n", string toolResultName = "tool-result.txt")
	{
		Directory.CreateDirectory(FolderIn(configDir, session));
		File.WriteAllText(TranscriptIn(configDir, session), transcript);
		File.WriteAllText(Path.Combine(FolderIn(configDir, session), toolResultName), "result");
	}

	public Task<JsonNode> Call(string tool, JsonObject arguments, SessionNodeModel caller) =>
		Server.HandleToolCallAsync(new JsonObject { ["name"] = tool, ["arguments"] = arguments }, caller);

	public static string TextOf(JsonNode result) => result["content"]![0]!["text"]!.GetValue<string>();

	public static async Task<string> ErrorOf(Func<Task<JsonNode>> action)
	{
		var ex = await Assert_ThrowsAny(action);
		return ex.Message;
	}

	private static async Task<Exception> Assert_ThrowsAny(Func<Task<JsonNode>> action)
	{
		try
		{
			await action();
		}
		catch (Exception ex)
		{
			return ex;
		}

		throw new InvalidOperationException("The call did not fail.");
	}
}
