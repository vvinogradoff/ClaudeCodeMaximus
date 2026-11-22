using System;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Engines;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services.Turns;

namespace TessynDesktop.Tests.Services.Engines;

/// <summary>
/// FR.33.1: the Claude engine turns claude's stream-json into the neutral <see cref="IEngineEventSink"/> calls. Each test feeds stream-json lines
/// (parsed by the real <see cref="ClaudeStreamEventParser"/>, as the process managers do) or the synthetic stderr events the managers emit, and
/// checks the calls that come out, including the engine's own retries.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClaudeSessionEngineEventMappingTests : IDisposable
{
	private const string AssistantWithActivity =
		"""{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"Let me look."},{"type":"text","text":"Reading."},{"type":"tool_use","id":"tu_1","name":"Read","input":{"file_path":"C:\\proj\\a.txt"}}]}}""";
	private const string ToolResult =
		"""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"tu_1","content":"first line\nsecond"}]}}""";
	private const string TaskProgress = """{"type":"system","subtype":"task_progress","description":"Scanning","last_tool_name":"Grep"}""";
	private const string Compact      = """{"type":"system","subtype":"compact"}""";
	private const string Success      = """{"type":"result","subtype":"success","session_id":"s-new","usage":{"input_tokens":3,"output_tokens":4},"total_cost_usd":0.5}""";
	private const string NoConversation = """{"type":"result","subtype":"error_during_execution","is_error":true,"errors":["No conversation found with session ID old"]}""";
	private const string OAuthRace    = """{"type":"result","subtype":"error_during_execution","is_error":true,"result":"API Error: OAuth session expired and could not be refreshed."}""";
	private const string BadCredentials = """{"type":"result","subtype":"error_during_execution","is_error":true,"result":"Invalid authentication credentials"}""";
	private const string RateLimited  = """{"type":"assistant","error":"rate_limit","message":{"content":[{"type":"text","text":"You've hit your limit"}]}}""";

	private readonly TurnEngineHarness _h = new();
	private readonly RecordingEngineSink _sink = new();
	private readonly ClaudeSessionEngine _engine;

	public ClaudeSessionEngineEventMappingTests()
	{
		_engine = new ClaudeSessionEngine(_h.Process, _h.Streaming, _h.Files, _h.Settings, _h.Status, new Lazy<IAgentMcpServer>(() => _h.Mcp));
	}

	public void Dispose() => _h.Dispose();

	private static readonly TurnSettings Settings = new("opus", "high", null, "Default", null, false, false, null, null);

	private static ClaudeStreamEvent SystemError(string text) =>
		new() { Type = Constants.Turns.EventTypes.System, Subtype = "error", IsError = true, Content = text };

	/// <summary>The next launch emits the events of these stream-json lines (or the given synthetic events) and exits.</summary>
	private void ScriptLaunch(params object[] lines) =>
		_h.Process.Script((_, onEvent, _) =>
		{
			foreach (var line in lines)
			{
				if (line is ClaudeStreamEvent evt)
					onEvent(evt);
				else
					foreach (var parsed in ClaudeStreamEventParser.ParseEvents((string)line))
						onEvent(parsed);
			}
			return Task.CompletedTask;
		});

	private Task Run(SessionNodeModel node, EngineTurnMode mode) =>
		_engine.RunTurnAsync(new EngineTurnRequest(node, "Hi+instructions", "Hi+instructions", node.ClaudeSessionId, Settings, null, mode), _sink, CancellationToken.None);

	[Fact]
	public async Task A_successful_turn_maps_text_thinking_tools_progress_compaction_and_the_result()
	{
		var node = TurnEngineHarness.Node();
		ScriptLaunch(AssistantWithActivity, ToolResult, TaskProgress, Compact, Success);

		await Run(node, EngineTurnMode.Interactive).Within();

		Assert.Equal(
		[
			"AssistantText(Reading., limit=False)",
			"Thinking(Let me look.)",
			@"ToolStarted(tu_1, Read, C:\proj\a.txt)",
			"ToolFinished(tu_1, first line, error=False)",
			"Progress([Grep] Scanning)",
			"Compacted",
			"TurnEnded(success=True, error=, auth=False, limit=False, conversation=s-new, tokens=3/4, cost=0.5)",
		], _sink.Calls);
		Assert.Equal("s-new", node.ClaudeSessionId);
		Assert.Equal("s-new", _engine.GetConversationId(node));
	}

	[Fact]
	public async Task The_turn_is_launched_with_the_settings_and_the_conversation_to_resume()
	{
		var node = TurnEngineHarness.Node(sessionId: "s0");
		ScriptLaunch(Success);

		await Run(node, EngineTurnMode.Interactive).Within();

		var call = Assert.Single(_h.Process.Calls);
		Assert.Equal("s0", call.SessionId);
		Assert.Equal("Hi+instructions", call.Message);
		Assert.Equal("opus", call.Model);
		Assert.Equal("high", call.Effort);
		Assert.Equal("claude-test", call.ClaudePath);
	}

	[Fact]
	public async Task An_invalid_credentials_result_ends_the_turn_with_an_auth_error()
	{
		ScriptLaunch(BadCredentials);

		await Run(TurnEngineHarness.Node(), EngineTurnMode.Interactive).Within();

		Assert.Equal(["TurnEnded(success=False, error=Invalid authentication credentials, auth=True, limit=False, conversation=, tokens=0/0, cost=0)"], _sink.Calls);
	}

	[Fact]
	public async Task A_rate_limited_assistant_message_is_marked_as_a_limit_error()
	{
		ScriptLaunch(RateLimited);

		await Run(TurnEngineHarness.Node(), EngineTurnMode.Interactive).Within();

		Assert.Equal(["AssistantText(You've hit your limit, limit=True)"], _sink.Calls);
	}

	[Fact]
	public async Task A_stderr_error_is_an_error_outside_the_result_classified_by_the_engine()
	{
		ScriptLaunch(SystemError("Weekly limit reached"), SystemError("Invalid authentication credentials"));

		await Run(TurnEngineHarness.Node(), EngineTurnMode.Interactive).Within();

		Assert.Equal(
		[
			"Error(Weekly limit reached, auth=False, limit=True)",
			"Error(Invalid authentication credentials, auth=True, limit=False)",
		], _sink.Calls);
	}

	[Fact]
	public async Task Interactive_no_conversation_found_detaches_the_session_and_retries_once_with_the_history()
	{
		var node = TurnEngineHarness.Node(sessionId: "old");
		_h.Files.Seed(Constants.SessionFile.RoleAssistant, "earlier answer");
		ScriptLaunch(NoConversation, SystemError("No conversation found with session ID old"));
		ScriptLaunch(Success);

		await Run(node, EngineTurnMode.Interactive).Within();

		Assert.Equal(
		[
			"RetryPending(LostConversation)",
			"Retrying(LostConversation)",
			"RetryRunning",
			"TurnEnded(success=True, error=, auth=False, limit=False, conversation=s-new, tokens=3/4, cost=0.5)",
		], _sink.Calls);
		Assert.Equal(2, _h.Process.Calls.Count);
		Assert.Null(_h.Process.Calls[1].SessionId);
		Assert.Contains("[Assistant]: earlier answer", _h.Process.Calls[1].Message);
		Assert.Equal("s-new", node.ClaudeSessionId);
	}

	[Fact]
	public async Task The_oauth_refresh_race_is_retried_once_silently_on_the_same_conversation()
	{
		var node = TurnEngineHarness.Node(sessionId: "s0");
		ScriptLaunch(OAuthRace);
		ScriptLaunch(Success);

		await Run(node, EngineTurnMode.Interactive).Within();

		Assert.Equal(
		[
			"RetryPending(TransientAuth)",
			"Retrying(TransientAuth)",
			"RetryRunning",
			"TurnEnded(success=True, error=, auth=False, limit=False, conversation=s-new, tokens=3/4, cost=0.5)",
		], _sink.Calls);
		Assert.Equal("s0", _h.Process.Calls[0].SessionId);
		Assert.Equal("s0", _h.Process.Calls[1].SessionId);
	}

	[Fact]
	public async Task Headless_no_conversation_found_is_an_error_and_its_stderr_echo_is_suppressed()
	{
		var node = TurnEngineHarness.Node(sessionId: "old");
		ScriptLaunch(SystemError("No conversation found with session ID old"), NoConversation, SystemError("something else broke"));

		await Run(node, EngineTurnMode.Headless).Within();

		Assert.Equal(
		[
			"TurnEnded(success=False, error=No conversation found with session ID old, auth=False, limit=False, conversation=, tokens=0/0, cost=0)",
			"Error(something else broke, auth=False, limit=False)",
		], _sink.Calls);
		Assert.Single(_h.Process.Calls);
		Assert.Equal("old", node.ClaudeSessionId);
	}

	[Fact]
	public async Task Headless_oauth_race_retries_once_and_a_headless_launch_never_passes_chrome()
	{
		var node = TurnEngineHarness.Node(sessionId: "s0");
		ScriptLaunch(OAuthRace);
		ScriptLaunch(Success);

		await _engine.RunTurnAsync(new EngineTurnRequest(node, "Hi", "Hi", "s0", Settings with { Chrome = true }, null, EngineTurnMode.Headless),
			_sink, CancellationToken.None).Within();

		Assert.Equal(2, _h.Process.Calls.Count);
		Assert.All(_h.Process.Calls, c => Assert.False(c.Chrome));
		Assert.Equal("RetryRunning", _sink.Calls[2]);
		Assert.Equal("s-new", node.ClaudeSessionId);
	}

	[Fact]
	public async Task A_headless_turn_in_the_live_process_observes_without_storing_the_session_id_and_retries_the_race()
	{
		var live   = new LiveProcessStreamingManager();
		var engine = new ClaudeSessionEngine(_h.Process, live, _h.Files, _h.Settings, _h.Status, new Lazy<IAgentMcpServer>(() => _h.Mcp));
		var node   = TurnEngineHarness.Node(sessionId: "kept", streaming: true);
		live.ScriptTurn(ClaudeStreamEventParser.ParseEvents(OAuthRace)[0]);
		live.ScriptTurn([.. ClaudeStreamEventParser.ParseEvents(AssistantWithActivity), .. ClaudeStreamEventParser.ParseEvents(Success)]);

		await engine.RunTurnInLiveProcessAsync(node, "headless", _sink, CancellationToken.None).Within();

		Assert.Equal(["headless", "headless"], live.Sends);
		Assert.Equal(
		[
			"RetryPending(TransientAuth)",
			"Retrying(TransientAuth)",
			"RetryRunning",
			"AssistantText(Reading., limit=False)",
			"Thinking(Let me look.)",
			@"ToolStarted(tu_1, Read, C:\proj\a.txt)",
			"TurnEnded(success=True, error=, auth=False, limit=False, conversation=s-new, tokens=3/4, cost=0.5)",
		], _sink.Calls);
		Assert.Equal("kept", node.ClaudeSessionId); // the process's primary sink stores it, not the observer
		Assert.Empty(_h.Process.Calls);
	}

	[Fact]
	public async Task A_persistent_turn_reports_the_process_start_on_the_turn_and_the_events_on_the_process_sink()
	{
		var node = TurnEngineHarness.Node(streaming: true);
		var processSink = new RecordingEngineSink();
		_h.Streaming.Script((primary, _) =>
		{
			foreach (var line in new[] { AssistantWithActivity, Success })
				foreach (var evt in ClaudeStreamEventParser.ParseEvents(line))
					primary(evt);
			return Task.CompletedTask;
		});

		await _engine.RunTurnAsync(new EngineTurnRequest(node, "Hi", "Hi", null, Settings, null, EngineTurnMode.Interactive, true, processSink),
			_sink, CancellationToken.None).Within();

		Assert.Equal(["PersistentProcessStarted"], _sink.Calls);
		Assert.Equal("AssistantText(Reading., limit=False)", processSink.Calls[0]);
		Assert.Equal("s-new", node.ClaudeSessionId);
		Assert.True(_engine.IsAlive(node.NodeId));
	}

	[Fact]
	public async Task The_preparation_writes_the_mcp_config_only_when_agent_tools_are_on()
	{
		var node = TurnEngineHarness.Node();

		var off = (ClaudeTurnPreparation?)await _engine.PrepareTurnAsync(node, Settings);
		_h.Settings.Settings.AgentToolsEnabled = true;
		var on = (ClaudeTurnPreparation?)await _engine.PrepareTurnAsync(node, Settings);

		Assert.Null(off!.McpConfigPath);
		Assert.NotNull(on!.McpConfigPath);
		Assert.False(on.StrictMcpConfig);
	}

	[Fact]
	public void Clear_conversation_forgets_the_session_id()
	{
		var node = TurnEngineHarness.Node(sessionId: "s0");

		_engine.ClearConversation(node);

		Assert.Null(_engine.GetConversationId(node));
	}

	[Fact]
	public void The_engine_reports_its_id_and_the_claude_capabilities()
	{
		Assert.Equal(Constants.SessionEngines.Claude, _engine.EngineId);
		Assert.True(_engine.Capabilities.PersistentProcess);
		Assert.False(_engine.Capabilities.InBandInterrupt);
		Assert.True(_engine.Capabilities.LocalModels);
	}
}
