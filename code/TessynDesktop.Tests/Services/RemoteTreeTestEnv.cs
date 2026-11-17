using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>Per-session canned activity; sessions without an entry have none.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ActivityByNodeService : ISessionActivityService
{
	public Dictionary<string, SessionActivityModel> ByNode { get; } = new();

	public SessionActivityModel GetActivity(SessionNodeModel node) =>
		ByNode.TryGetValue(node.NodeId, out var activity) ? activity : new SessionActivityModel(null, null, null, null, false);

	public void Set(string nodeId, DateTimeOffset? lastActivity, DateTimeOffset? lastPrompt = null, bool hasDraft = false) =>
		ByNode[nodeId] = new SessionActivityModel(lastActivity, lastPrompt ?? lastActivity, lastActivity, null, hasDraft);
}

/// <summary><see cref="ISessionSearchService"/> fake: canned file-name matches, or a failure.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeSessionSearchService : ISessionSearchService
{
	public HashSet<string> Matches { get; } = new();
	public Exception? Failure { get; set; }

	public IReadOnlySet<string> FindMatchingSessionFiles(string query) => Failure != null ? throw Failure : Matches;
}

/// <summary>
/// The real tree (temp-dir settings + <see cref="SessionRegistry"/>), the three B1 remote modules and a dispatcher over them,
/// with fakes for everything that is not tree structure (activity, attention, schedules, tags, search index).
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class RemoteTreeTestEnv : IDisposable
{
	public string Root { get; }
	public AppSettingsService Settings { get; }
	public SessionRegistry Registry { get; }
	public ActivityByNodeService Activity { get; } = new();
	public FakeAttentionService Attention { get; } = new();
	public InProcFakeScheduler Scheduler { get; } = new();
	public FakeSessionTagService Tags { get; } = new();
	public InProcFakeIndexService Index { get; } = new();
	public FakeSessionSearchService FileSearch { get; } = new();
	public TreeVersionTracker Version { get; } = new();
	public SessionStateBuilder States { get; }
	public FakeClaudeSessionStatusService Status { get; } = new();
	public SessionResumabilityCache Resumable { get; }
	public TdcpDispatcher Dispatcher { get; }

	public RemoteTreeTestEnv()
	{
		Root     = Path.Combine(Path.GetTempPath(), "cm_test_b1_" + Path.GetRandomFileName());
		Directory.CreateDirectory(Root);
		Settings = new AppSettingsService(Root);
		Registry = new SessionRegistry(Settings, new FakeSessionFileService(), new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
		var reader = new RemoteTreeReader(Settings);
		Resumable = new SessionResumabilityCache(reader, Status);
		States   = new SessionStateBuilder(Registry, new FakeStreamingManagerForTurnService(), Attention, Activity, resumable: Resumable);

		Dispatcher = new TdcpDispatcher(
		[
			new TreeRemoteRpcModule(reader, Registry, States, Activity, Version, new FakeDirectoryLabelService(), Index, FileSearch),
			new RecentRemoteRpcModule(reader, States, Activity, Attention, Scheduler, new FakeDirectoryLabelService(), Registry, Settings),
			new TagsRemoteRpcModule(reader, Tags, Activity, States, new FakeDirectoryLabelService()),
		]);
	}

	public void Dispose()
	{
		Resumable.Dispose();
		Directory.Delete(Root, recursive: true);
	}

	public DirectoryNodeModel AddDirectory(string name) => Registry.AddDirectory(Path.Combine(Root, name));

	/// <summary>Adds a session to the directory (or to the group when given), optionally as a worker of <paramref name="supervisorNodeId"/>.</summary>
	public SessionNodeModel AddSession(DirectoryNodeModel dir, string name, GroupNodeModel? group = null, string? supervisorNodeId = null, string? externalId = null)
	{
		var session = group == null ? Registry.AddSession(dir, name) : Registry.AddSessionToGroup(group, name);
		lock (Settings.SyncRoot)
		{
			session.SupervisorNodeId = supervisorNodeId;
			session.ExternalId       = externalId;
		}
		return session;
	}

	/// <summary>Sends a JSON-RPC call through the dispatcher on the relay transport (only remote-callable methods) and returns the whole response.</summary>
	public async Task<JsonNode> CallRawAsync(string method, string paramsJson = "{}")
	{
		var request = $$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{paramsJson}}}""";
		return await Dispatcher.HandleTextAsync(request, TdcpTransport.Relay, CancellationToken.None);
	}

	/// <summary>Like <see cref="CallRawAsync"/> but returns <c>result</c>, failing the test on an error response.</summary>
	public async Task<JsonNode> CallAsync(string method, string paramsJson = "{}")
	{
		var response = await CallRawAsync(method, paramsJson);
		if (response["error"] != null)
			throw new InvalidOperationException("Unexpected error response: " + response["error"]);
		return response["result"]!;
	}

	public static int ErrorCode(JsonNode response) => response["error"]!["code"]!.GetValue<int>();
}
