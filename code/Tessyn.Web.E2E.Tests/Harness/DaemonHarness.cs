using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>
/// The daemon side of the stack: the real Core services over a temp data root (settings, session files, attention ledger, tag store,
/// drafts, a real FTS search index), wired like <c>App.axaml.cs</c>, with fakes only for the claude process, the profile CLI,
/// the usage endpoint, Telegram, the MCP server and the UI dispatcher.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonHarness : IDisposable
{
	private readonly ServiceProvider _services;

	private DaemonHarness(string root, ServiceProvider services, FakeClaudeProcessManager process, SeedModel seed, string projectsDir)
	{
		Root = root;
		_services = services;
		Process = process;
		Seed = seed;
		ProjectsDir = projectsDir;
	}

	public string Root { get; }
	public string ProjectsDir { get; }
	public FakeClaudeProcessManager Process { get; }
	public SeedModel Seed { get; }

	public IServiceProvider Services => _services;
	public T Get<T>() where T : notnull => _services.GetRequiredService<T>();

	public TdcpDispatcher Dispatcher => Get<TdcpDispatcher>();
	public IRemoteEventBus Bus => Get<IRemoteEventBus>();
	public ISessionRegistry Registry => Get<ISessionRegistry>();
	public IAppSettingsService Settings => Get<IAppSettingsService>();
	public ISessionFileService Files => Get<ISessionFileService>();
	public IAttentionService Attention => Get<IAttentionService>();

	public static async Task<DaemonHarness> CreateAsync()
	{
		var root = Path.Combine(Path.GetTempPath(), "tessyn-e2e-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		var projectsDir = Path.Combine(root, "claude-projects");
		Directory.CreateDirectory(projectsDir);
		var process = new FakeClaudeProcessManager();

		var services = new ServiceCollection();
		Register(services, root, projectsDir, process);
		var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false });

		var settings = provider.GetRequiredService<IAppSettingsService>();
		settings.Load();
		lock (settings.SyncRoot)
		{
			settings.Settings.ClaudePath = "claude-fake";
			settings.Settings.AgentToolsEnabled = false;
			settings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "profile_1", DisplayName = "work@example.test" });
		}

		settings.Save();

		var seed = await SeedAsync(provider, root, projectsDir);
		var harness = new DaemonHarness(root, provider, process, seed, projectsDir);

		// The publisher is what turns domain events into remote notifications; the desktop starts it at boot.
		provider.GetRequiredService<IRemoteNotificationPublisher>().Start();
		return harness;
	}

	public void Dispose()
	{
		try
		{
			_services.Dispose();
		}
		catch (Exception)
		{
			// Best effort: some singletons own timers that may already be gone.
		}

		try
		{
			Directory.Delete(Root, true);
		}
		catch (IOException)
		{
			// Temp data only.
		}
		catch (UnauthorizedAccessException)
		{
			// Temp data only.
		}
	}

	private static void Register(IServiceCollection s, string root, string projectsDir, FakeClaudeProcessManager process)
	{
		s.AddSingleton<IUiDispatcher, ImmediateUiDispatcher>();
		s.AddSingleton<IAppSettingsService>(_ => new AppSettingsService(root));
		s.AddSingleton<IDirectoryLabelService, DirectoryLabelService>();
		s.AddSingleton<ISessionFileService, SessionFileService>();
		s.AddSingleton(_ => new AttentionLedgerStore(Path.Combine(root, "attention")));
		s.AddSingleton<IAttentionService, AttentionService>();
		s.AddSingleton(_ => new SessionTagStore(Path.Combine(root, "tags")));
		s.AddSingleton<ISessionTagService, SessionTagService>();
		s.AddSingleton<IDraftService>(_ => new DraftService(Path.Combine(root, "drafts")));
		s.AddSingleton<IClaudeProcessManager>(process);
		s.AddSingleton<IClaudeStreamingSessionManager, FakeStreamingManager>();
		s.AddSingleton<IClaudeSessionStatusService, FakeSessionStatusService>();
		s.AddSingleton<ISessionSearchService, SessionSearchService>();
		s.AddSingleton<IClaudeProfileService>(_ => new FakeProfileService(root));
		s.AddSingleton<IClaudeModelService, FakeModelService>();
		s.AddSingleton<IClaudeUsageService, FakeDesktopUsageService>();
		s.AddSingleton<IClaudeUsageFetcher, FakeUsageFetcher>();
		s.AddSingleton<IClaudeSessionImportService>(_ => new ClaudeSessionImportService([projectsDir]));
		s.AddSingleton<IClaudeAssistService, ClaudeAssistService>();
		s.AddSingleton<IClaudeSessionIndexService>(sp => new ClaudeSessionIndexService(
			sp.GetRequiredService<IClaudeSessionImportService>(),
			sp.GetRequiredService<IClaudeAssistService>(),
			Path.Combine(root, "index.db"),
			[projectsDir]));
		s.AddSingleton<ISessionRegistry, SessionRegistry>();
		s.AddSingleton<ISessionTurnService, SessionTurnService>();
		s.AddSingleton<ISessionCompactionService, SessionCompactionService>();
		s.AddSingleton<IAgentMcpServer, FakeAgentMcpServer>();
		s.AddSingleton<ITelegramBridgeService, FakeTelegramBridge>();
		s.AddSingleton<ISchedulerService, SchedulerService>();
		s.AddSingleton(sp => new Lazy<ISessionTurnService>(sp.GetRequiredService<ISessionTurnService>));
		s.AddSingleton(sp => new Lazy<IAgentMcpServer>(sp.GetRequiredService<IAgentMcpServer>));
		s.AddSingleton(sp => new Lazy<ITelegramBridgeService>(sp.GetRequiredService<ITelegramBridgeService>));
		s.AddSingleton<IDaemonClient, InProcDaemonClient>();

		s.AddTransient<IRemoteRpcModule, TdcpLoopbackRpcModule>();
		s.AddSingleton<IRemoteRpcModule, ClientRpcModule>();
		s.AddSingleton<TdcpDispatcher>();
		s.AddTransient<IRemoteRpcModule, SessionCompactRpcModule>();
		s.AddSingleton<IRemoteEventBus, RemoteEventBus>();
		s.AddSingleton<ITreeVersionTracker, TreeVersionTracker>();
		s.AddSingleton<ISessionEpochTracker, SessionEpochTracker>();
		s.AddSingleton<ISessionActivityService, SessionActivityService>();
		s.AddTransient<ISessionStateBuilder, SessionStateBuilder>();
		s.AddTransient<IRemoteTreeReader, RemoteTreeReader>();
		s.AddTransient<IRemoteRpcModule, TreeRemoteRpcModule>();
		s.AddTransient<IRemoteRpcModule, RecentRemoteRpcModule>();
		s.AddTransient<IRemoteRpcModule, TagsRemoteRpcModule>();
		s.AddSingleton<IRemoteNotificationPublisher, RemoteNotificationPublisher>();
		s.AddSingleton<IProfileLabelService, ProfileLabelService>();
		s.AddSingleton<ISessionContentStore>(sp => new SessionContentStore(sp.GetRequiredService<ISessionFileService>(), Path.Combine(root, "entry-index")));
		s.AddSingleton<ISessionEntryReader, SessionEntryReader>();
		s.AddSingleton<ISessionSendService, SessionSendService>();
		s.AddSingleton<IRemoteUsageService, RemoteUsageService>();
		s.AddTransient<ITurnOptionsResolver, TurnOptionsResolver>();
		s.AddTransient<IAttentionReplyService, AttentionReplyService>();
		s.AddTransient<IRemoteRpcModule, SessionRpcModule>();
		s.AddTransient<IRemoteRpcModule, AttentionRpcModule>();
		s.AddTransient<IRemoteRpcModule, CatalogRpcModule>();
		s.AddTransient<IRemoteRpcModule, UsageRpcModule>();
	}

	private static async Task<SeedModel> SeedAsync(ServiceProvider provider, string root, string projectsDir)
	{
		var registry = provider.GetRequiredService<ISessionRegistry>();
		var settings = provider.GetRequiredService<IAppSettingsService>();
		var files = provider.GetRequiredService<ISessionFileService>();
		var attention = provider.GetRequiredService<IAttentionService>();
		var tags = provider.GetRequiredService<ISessionTagService>();

		var alphaPath = Path.Combine(root, SeedModel.AlphaLabel);
		var betaPath = Path.Combine(root, SeedModel.BetaLabel);
		Directory.CreateDirectory(alphaPath);
		Directory.CreateDirectory(betaPath);

		var alpha = registry.AddDirectory(alphaPath);
		var beta = registry.AddDirectory(betaPath);
		var supervisor = registry.AddSession(alpha, SeedModel.SupervisorName);
		var workerOne = registry.AddSession(alpha, SeedModel.WorkerOneName);
		var workerTwo = registry.AddSession(alpha, SeedModel.WorkerTwoName);
		var favourite = registry.AddSession(alpha, SeedModel.FavoriteName);
		var group = registry.AddGroup(alpha, SeedModel.GroupName);
		var backend = registry.AddSessionToGroup(group, SeedModel.BackendName);
		var nested = registry.AddGroupToGroup(group, SeedModel.NestedGroupName);
		var deep = registry.AddSessionToGroup(nested, SeedModel.DeepName);
		var plain = registry.AddSession(beta, SeedModel.BetaName);
		var target = registry.AddSession(beta, SeedModel.SearchTargetName);

		var externalId = Guid.NewGuid().ToString();
		lock (settings.SyncRoot)
		{
			workerOne.SupervisorNodeId = supervisor.NodeId;
			workerTwo.SupervisorNodeId = supervisor.NodeId;
			favourite.IsFavorite = true;
			target.ExternalId = externalId;
		}

		settings.Save();

		WriteClaudeJsonl(projectsDir, betaPath, externalId);
		await provider.GetRequiredService<IClaudeSessionIndexService>().ReindexAsync();

		files.AppendMessage(supervisor.FileName, "USER", "Plan the rollout", modelId: null, profileName: "[Sys] e2e@example.test");
		files.AppendMessage(supervisor.FileName, "ASSISTANT", "Here is the plan for the rollout.", modelId: "default", effort: "default");
		files.AppendMessage(favourite.FileName, "USER", "Hello favourite");
		files.AppendMessage(favourite.FileName, "ASSISTANT", "Hello back");
		files.AppendMessage(plain.FileName, "USER", "Beta question");
		files.AppendMessage(plain.FileName, "ASSISTANT", "Beta answer");

		var outcomes = attention.Post(supervisor,
		[
			new AttentionPostRequestModel { Kind = AttentionKind.Ask, Title = SeedModel.AskTitle, Body = "Postgres or SQLite?", Options = ["Postgres", "SQLite"], Recommendation = "Postgres" },
			new AttentionPostRequestModel { Kind = AttentionKind.Todo, Title = SeedModel.TodoTitle, Body = "Before Friday." },
			new AttentionPostRequestModel { Kind = AttentionKind.Alert, Title = SeedModel.AlertTitle, Body = "92% used." },
		]);

		tags.Edit(backend, [SeedModel.PullRequestUrl], []);
		tags.Edit(plain, [SeedModel.PullRequestUrl, SeedModel.RepositoryUrl], []);

		return new SeedModel
		{
			AlphaDirectoryId = alpha.NodeId,
			BetaDirectoryId = beta.NodeId,
			GroupId = group.NodeId,
			NestedGroupId = nested.NodeId,
			SupervisorId = supervisor.NodeId,
			WorkerOneId = workerOne.NodeId,
			WorkerTwoId = workerTwo.NodeId,
			FavoriteId = favourite.NodeId,
			BackendId = backend.NodeId,
			DeepId = deep.NodeId,
			BetaId = plain.NodeId,
			SearchTargetId = target.NodeId,
			AskItemId = outcomes[0].Item.Id,
			TodoItemId = outcomes[1].Item.Id,
			AlertItemId = outcomes[2].Item.Id,
			AgentToken = supervisor.AgentToken,
			ClaudeSessionId = externalId,
			AlphaPath = alphaPath,
		};
	}

	private static void WriteClaudeJsonl(string projectsDir, string workingDirectory, string externalId)
	{
		var slugDir = Path.Combine(projectsDir, TessynDesktop.Constants.ClaudeSessions.BuildProjectSlug(workingDirectory));
		Directory.CreateDirectory(slugDir);
		var user = JsonSerializer.Serialize(new { type = "user", timestamp = "2026-01-01T00:00:00Z", message = new { role = "user", content = "Find the " + SeedModel.SearchWord + " in the aviary" } });
		var assistant = "{\"type\":\"assistant\",\"timestamp\":\"2026-01-01T00:01:00Z\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"The " + SeedModel.SearchWord + " is in cage four.\"}]}}";
		File.WriteAllLines(Path.Combine(slugDir, externalId + ".jsonl"), [user, assistant]);
	}
}
