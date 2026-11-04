using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Turn service that records every run and cancel, so short-id tests can check the resolved full NodeId.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class RecordingTurnService : ISessionTurnService
{
	private readonly SemaphoreSlim _lock = new(1, 1);

	public List<string> RunNodeIds { get; } = new();
	public List<string> CancelledNodeIds { get; } = new();

	public SemaphoreSlim GetTurnLock(string nodeId) => _lock;

	public Task<TurnResultModel> RunTurnAsync(SessionNodeModel node, string prompt,
		TurnSource source, CancellationToken cancellationToken = default)
	{
		lock (RunNodeIds) RunNodeIds.Add(node.NodeId);
		return Task.FromResult(new TurnResultModel(AssistantText: "ok", SessionId: null));
	}

	/// <summary>What <see cref="CancelTurn"/> reports: whether a turn was running.</summary>
	public bool CancelResult { get; set; } = true;

	public bool CancelTurn(string nodeId)
	{
		CancelledNodeIds.Add(nodeId);
		return CancelResult;
	}

	public string? ResolveProfileName(SessionNodeModel node) => null;
}

/// <summary>
/// TC.15.7 (the <c>list_sessions</c> tool surface) and the <c>Tools_*</c> tests of TC.15.6 (Short Node Ids
/// accepted by every tool), over a real <see cref="SessionRegistry"/> — FR.15.13, FR.15.11.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AgentMcpServerListSessionsTests : IDisposable
{
	private const string HaikuModel = "claude-haiku-4-5-20251001";

	private readonly string _tempDir;
	private readonly AppSettingsService _settings;
	private readonly FakeSessionFileService _fileService = new();
	private readonly SessionRegistry _registry;
	private readonly RecordingTurnService _turns = new();
	private readonly RecordingScheduler _scheduler = new();
	private readonly AgentMcpServer _server;
	private readonly DirectoryNodeModel _dir;

	public AgentMcpServerListSessionsTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
		_settings = new AppSettingsService(_tempDir);
		_registry = new SessionRegistry(_settings, _fileService, new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
		_server = new AgentMcpServer(
			_settings,
			new Lazy<ISessionTurnService>(() => _turns),
			new Lazy<ISchedulerService>(() => _scheduler),
			_registry,
			_fileService,
			modelService: null!,
			tdcpServer: null!,
			configMerger: null!,
			new FakeTelegramBridge(),
			new FakeNotificationService(),
			new FakeDirectoryLabelService(),
			new AttentionService(new AttentionLedgerStore(Path.Combine(_tempDir, "attention")), _fileService),
			TagTestEnv.Unused());
		_dir = _registry.AddDirectory(Path.Combine(_tempDir, "dir"));
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	// ── Helpers ───────────────────────────────────────────────────────────────

	private SessionNodeModel AddSession(string name, string? nodeId = null, DirectoryNodeModel? dir = null)
	{
		var session = _registry.AddSession(dir ?? _dir, name);
		if (nodeId != null)
			session.NodeId = nodeId;
		session.ModelId = HaikuModel;
		_fileService.LastWriteTimes[session.FileName] = DateTimeOffset.UtcNow.AddMinutes(-5);
		return session;
	}

	private Task<JsonNode> Call(string tool, JsonObject arguments, SessionNodeModel caller) =>
		_server.HandleToolCallAsync(new JsonObject { ["name"] = tool, ["arguments"] = arguments }, caller);

	private static string TextOf(JsonNode result) => result["content"]![0]!["text"]!.GetValue<string>();

	private async Task<string> ListText(SessionNodeModel caller, JsonObject? arguments = null) =>
		TextOf(await Call("list_sessions", arguments ?? new JsonObject(), caller));

	private static string ShortOf(SessionNodeModel session) => session.NodeId[..Constants.Agent.ShortNodeIdMinLength];

	private static async Task<string> ErrorOf(Func<Task<JsonNode>> action)
	{
		var ex = await Assert.ThrowsAnyAsync<Exception>(action);
		return ex.Message;
	}

	private static JsonNode ToolsList()
	{
		var method = typeof(AgentMcpServer).GetMethod("HandleToolsList", BindingFlags.NonPublic | BindingFlags.Static)!;
		return (JsonNode)method.Invoke(null, null)!;
	}

	// ── TC.15.7 ───────────────────────────────────────────────────────────────

	[Fact]
	public void ToolsList_ListSessions_DeclaresAllArguments()
	{
		var tool = ToolsList()["tools"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "list_sessions")!;
		var properties = tool["inputSchema"]!["properties"]!.AsObject();

		Assert.Equal(
			new[] { "activeWithinHours", "includeWorkers", "limit", "nodeId", "offset", "query", "running", "scope" },
			properties.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal));
		Assert.Equal(
			new[] { "project", "all", "children", "subtree" },
			properties["scope"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()));
		Assert.Equal("boolean", properties["running"]!["type"]!.GetValue<string>());
		Assert.Equal("integer", properties["activeWithinHours"]!["type"]!.GetValue<string>());
		Assert.Contains("30 rows per page", tool["description"]!.GetValue<string>());
	}

	[Fact]
	public async Task ListSessions_NoArgs_YouLineHeaderRowsFooter()
	{
		var caller = AddSession("Boss");
		var worker = AddSession("Helper");
		worker.OrchestrationDepth = 1;
		worker.SupervisorNodeId   = caller.NodeId;
		var other = AddSession("Other");
		var now   = DateTimeOffset.UtcNow;
		_fileService.LastWriteTimes[caller.FileName] = now.AddMinutes(-1);
		_fileService.LastWriteTimes[worker.FileName] = now.AddMinutes(-2);
		_fileService.LastWriteTimes[other.FileName]  = now.AddMinutes(-3);

		var text  = await ListText(caller);
		var lines = text.Split('\n');

		Assert.StartsWith($"You: {ShortOf(caller)}", lines[0]);
		Assert.Contains("\"Boss\" - top-level session in ", lines[0]);
		Assert.Contains("top-level sessions, 1 workers", lines[1]);
		Assert.Contains("Boss", lines[2]);
		Assert.EndsWith("(you)", lines[2].TrimEnd());
		Assert.StartsWith("  " + ShortOf(worker), lines[3]);
		Assert.Contains("Other", lines[4]);
		Assert.Equal("Rows 1-3 of 3.", lines[5]);
		Assert.Equal("Other projects: scope=all. Narrow: query=, running=, activeWithinHours=.", lines[6]);
	}

	[Theory]
	[InlineData("galaxy")]
	[InlineData("2")]
	[InlineData("project,all")]
	public async Task ListSessions_InvalidScope_Error(string scope)
	{
		var caller = AddSession("Boss");

		var message = await ErrorOf(() => Call("list_sessions", new JsonObject { ["scope"] = scope }, caller));

		Assert.Equal($"Unknown scope '{scope}'. Use project, all, children or subtree.", message);
	}

	[Fact]
	public async Task ListSessions_ScopeCaseAndWhitespaceTolerated()
	{
		var caller = AddSession("Boss");

		var text = await ListText(caller, new JsonObject { ["scope"] = "  ALL " });

		Assert.DoesNotContain("Other projects", text);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-3)]
	public async Task ListSessions_NonPositiveActiveWithinHours_Error(int hours)
	{
		var caller = AddSession("Boss");

		var message = await ErrorOf(() => Call("list_sessions", new JsonObject { ["activeWithinHours"] = hours }, caller));

		Assert.Contains("activeWithinHours", message);
	}

	[Fact]
	public async Task ListSessions_UnknownNodeId_Error()
	{
		var caller = AddSession("Boss");

		var message = await ErrorOf(() => Call(
			"list_sessions", new JsonObject { ["scope"] = "children", ["nodeId"] = "deadbeefdead" }, caller));

		Assert.Contains("deadbeefdead", message);
	}

	[Fact]
	public async Task ListSessions_WrongArgumentType_ErrorNamesTheArgument()
	{
		var caller = AddSession("Boss");

		var message = await ErrorOf(() => Call("list_sessions", new JsonObject { ["running"] = "yes" }, caller));

		Assert.Contains("'running'", message);
	}

	[Fact]
	public async Task ListSessions_ChildrenScope_ListsWorkersOfResolvedShortId()
	{
		var boss   = AddSession("Boss");
		var worker = AddSession("Helper");
		worker.OrchestrationDepth = 1;
		worker.SupervisorNodeId   = boss.NodeId;
		var caller = AddSession("Caller");

		var text = await ListText(caller, new JsonObject { ["scope"] = "children", ["nodeId"] = ShortOf(boss) });

		Assert.Contains($"Workers of {ShortOf(boss)} \"Boss\" - 1 direct, 1 in subtree", text);
		Assert.Contains("Helper", text);
		Assert.DoesNotContain("Other projects", text);
	}

	[Fact]
	public async Task ListSessions_2000SessionTree_DefaultPageUnder5000Chars_FullPageUnder30000Chars()
	{
		SessionNodeModel? caller = null;
		var created = 0;
		var tree = _settings.Settings.Tree;

		for (var d = 0; d < 20; d++)
		{
			var path = Path.Combine(_tempDir, $"project-{d:00}");
			var dir  = new DirectoryNodeModel { Path = path };
			var tops = new List<SessionNodeModel>();

			for (var i = 0; i < 100; i++)
			{
				var isWorker = i >= 60;
				var n        = created++;
				var session  = new SessionNodeModel
				{
					Name               = $"{(isWorker ? "Worker" : "Session")} {n:0000} - a realistic, fairly long session name about refactoring the listing",
					FileName           = $"s{n}.txt",
					NodeId             = Guid.NewGuid().ToString("N"),
					WorkingDirectory   = path,
					OrchestrationDepth = isWorker ? 1 : 0,
					SupervisorNodeId   = isWorker ? tops[i % 60].NodeId : null,
				};
				_fileService.LastWriteTimes[session.FileName] = DateTimeOffset.UtcNow.AddMinutes(-n);
				dir.Sessions.Add(session);
				if (!isWorker)
					tops.Add(session);
			}

			tree.Add(dir);
			caller ??= tops[0];
		}

		var defaultPage = await ListText(caller!);
		var fullPage    = await ListText(caller!, new JsonObject { ["scope"] = "all", ["includeWorkers"] = true, ["limit"] = 200 });

		Assert.Equal(2000, created);
		Assert.True(defaultPage.Length < 5000, $"default page was {defaultPage.Length} chars");
		Assert.True(fullPage.Length < 30000, $"full page was {fullPage.Length} chars");
		Assert.Contains("Rows 1-200 of ", fullPage);
		Assert.Contains("Next page: offset=200.", fullPage);
	}

	[Fact]
	public void OrchestrationSkill_WorkerSection_NoLongerSendsWorkersToFullListing()
	{
		var skill  = Constants.Agent.OrchestrationSkillMarkdown;
		var worker = skill[skill.IndexOf("## As a worker", StringComparison.Ordinal)..];

		Assert.DoesNotContain("check `list_sessions`", worker);
		Assert.Contains("`You:` line of `list_sessions`", worker);
		Assert.Contains("find your workers", skill);
		Assert.DoesNotContain("inspect the tree", skill);
		Assert.DoesNotContain("inspect the session tree", Constants.Instructions.NoWorkflowTool);
	}

	// ── TC.15.6 ───────────────────────────────────────────────────────────────

	[Theory]
	[InlineData("send_to_session")]
	[InlineData("read_session")]
	[InlineData("stop_session")]
	[InlineData("set_session_model")]
	[InlineData("schedule_wake")]
	[InlineData("spawn_session")]
	[InlineData("list_sessions")]
	public async Task Tools_AcceptShortNodeIds(string tool)
	{
		var caller = AddSession("Caller");
		var other  = _registry.AddDirectory(Path.Combine(_tempDir, "dir2"));
		var target = AddSession("Target", dir: other);
		var prefix = ShortOf(target);

		switch (tool)
		{
			case "send_to_session":
				var sent = await Call(tool, new JsonObject { ["nodeId"] = prefix, ["prompt"] = "hi", ["mode"] = "wait" }, caller);
				Assert.Equal("ok", TextOf(sent));
				Assert.Equal(target.NodeId, Assert.Single(_turns.RunNodeIds));
				break;

			case "read_session":
				_fileService.Entries[target.FileName] = new[]
				{
					new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = Constants.SessionFile.RoleUser, Content = "marker-text" },
				};
				Assert.Contains("marker-text", TextOf(await Call(tool, new JsonObject { ["nodeId"] = prefix }, caller)));
				break;

			case "stop_session":
				var stopped = TextOf(await Call(tool, new JsonObject { ["nodeId"] = prefix }, caller));
				Assert.Equal(target.NodeId, Assert.Single(_turns.CancelledNodeIds));
				Assert.Contains(target.NodeId, stopped);
				break;

			case "set_session_model":
				await Call(tool, new JsonObject { ["nodeId"] = prefix, ["model"] = HaikuModel }, caller);
				Assert.Equal(HaikuModel, target.ModelId);
				break;

			case "schedule_wake":
				await Call(tool, new JsonObject
				{
					["when"]   = new JsonObject { ["inSeconds"] = 60.0 },
					["prompt"] = "later",
					["target"] = prefix,
				}, caller);
				Assert.Equal(target.NodeId, Assert.Single(_scheduler.Added).TargetNodeId);
				break;

			case "spawn_session":
				await Call(tool, new JsonObject { ["name"] = "Child", ["parentNodeId"] = prefix }, caller);
				Assert.Contains(other.Sessions, s => s.Name == "Child");
				break;

			default:
				var listed = await ListText(caller, new JsonObject { ["scope"] = "children", ["nodeId"] = prefix });
				Assert.Contains($"Workers of {ShortOf(target)} \"Target\"", listed);
				break;
		}
	}

	[Fact]
	public async Task Tools_AmbiguousPrefix_ErrorListsMatches()
	{
		var caller = AddSession("Caller");
		var first  = AddSession("First", "abcdef0000000000000000000000000a");
		var second = AddSession("Second", "abcdef0000000000000000000000000b");

		var message = await ErrorOf(() => Call("read_session", new JsonObject { ["nodeId"] = "abcdef" }, caller));

		Assert.Contains("Session id 'abcdef' matches 2 sessions:", message);
		Assert.Contains($"{first.NodeId} \"First\"", message);
		Assert.Contains($"{second.NodeId} \"Second\"", message);
		Assert.EndsWith("Use more characters.", message);
	}

	[Fact]
	public async Task Tools_PrefixTooShort_Error()
	{
		var caller = AddSession("Caller");

		var message = await ErrorOf(() => Call("stop_session", new JsonObject { ["nodeId"] = caller.NodeId[..5] }, caller));

		Assert.Equal($"Session id '{caller.NodeId[..5]}' is too short: use at least 6 characters.", message);
		Assert.Empty(_turns.CancelledNodeIds);
	}

	[Fact]
	public async Task Tools_IdArgumentsTrimmed()
	{
		var caller = AddSession("Caller");
		var other  = _registry.AddDirectory(Path.Combine(_tempDir, "dir2"));
		var target = AddSession("Target", dir: other);
		var padded = "  " + ShortOf(target) + " \t";

		await Call("send_to_session", new JsonObject { ["nodeId"] = padded, ["prompt"] = "hi" }, caller);
		await Call("stop_session", new JsonObject { ["nodeId"] = padded }, caller);
		await Call("set_session_model", new JsonObject { ["nodeId"] = padded, ["model"] = HaikuModel }, caller);
		await Call("schedule_wake", new JsonObject { ["when"] = new JsonObject { ["inSeconds"] = 60.0 }, ["target"] = padded }, caller);
		await Call("spawn_session", new JsonObject { ["name"] = "Child", ["parentNodeId"] = padded }, caller);
		var read   = TextOf(await Call("read_session", new JsonObject { ["nodeId"] = padded }, caller));
		var listed = await ListText(caller, new JsonObject { ["scope"] = "children", ["nodeId"] = padded });

		Assert.Equal(target.NodeId, Assert.Single(_turns.RunNodeIds));
		Assert.Equal(target.NodeId, Assert.Single(_turns.CancelledNodeIds));
		Assert.Equal(target.NodeId, Assert.Single(_scheduler.Added).TargetNodeId);
		Assert.Contains(other.Sessions, s => s.Name == "Child");
		Assert.Contains("no conversation entries", read);
		Assert.Contains($"Workers of {ShortOf(target)}", listed);
	}

	[Fact]
	public async Task Tools_AmbiguousPrefix_NamesAreCutTo60Characters()
	{
		var caller = AddSession("Caller");
		AddSession(new string('x', 100), "abcdef0000000000000000000000000a");
		AddSession("Second", "abcdef0000000000000000000000000b");

		var message = await ErrorOf(() => Call("read_session", new JsonObject { ["nodeId"] = "abcdef" }, caller));

		Assert.Contains("\"" + new string('x', Constants.Agent.ListSessionsNameMaxLength - 3) + "...\"", message);
		Assert.DoesNotContain(new string('x', Constants.Agent.ListSessionsNameMaxLength - 2), message);
	}

	[Fact]
	public async Task Tools_UnknownId_KeepsEachToolsNotFoundWording()
	{
		var caller = AddSession("Caller");
		const string unknown = "ffffffffffff";
		_turns.CancelResult = false;

		Assert.Equal($"Session not found: {unknown}",
			await ErrorOf(() => Call("send_to_session", new JsonObject { ["nodeId"] = unknown, ["prompt"] = "hi" }, caller)));
		Assert.Equal($"Session not found: {unknown}",
			await ErrorOf(() => Call("read_session", new JsonObject { ["nodeId"] = unknown }, caller)));
		Assert.Equal($"Session not found: {unknown}",
			await ErrorOf(() => Call("set_session_model", new JsonObject { ["nodeId"] = unknown, ["model"] = HaikuModel }, caller)));
		Assert.Equal($"Target session not found: {unknown}",
			await ErrorOf(() => Call("schedule_wake", new JsonObject
			{
				["when"] = new JsonObject { ["inSeconds"] = 5.0 }, ["target"] = unknown,
			}, caller)));
		Assert.Equal($"Parent node not found: {unknown}",
			await ErrorOf(() => Call("spawn_session", new JsonObject { ["name"] = "x", ["parentNodeId"] = unknown }, caller)));

		// stop_session passes an unknown id on, so a session removed mid-turn can still be cancelled.
		var stopped = TextOf(await Call("stop_session", new JsonObject { ["nodeId"] = unknown }, caller));
		Assert.Equal($"No active turn for {unknown}.", stopped);
		Assert.Equal(unknown, Assert.Single(_turns.CancelledNodeIds));
	}
}
