using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TessynDesktop.Models.Engines;
using TessynDesktop.Tests.Codex.Fakes;
using Xunit;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>
/// The Codex engine's turn on the in-process fake app-server: the first turn (TC.CX.10), later turns and resumes (TC.CX.12), resume failures
/// (TC.CX.13), the event mapping (TC.CX.14) and the worker result (TC.CX.15).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexSessionEngineTurnTests : IAsyncLifetime
{
	private readonly CodexEngineFixture _f = new();

	public Task InitializeAsync() => Task.CompletedTask;

	public async Task DisposeAsync() => await _f.DisposeAsync();

	[Fact]
	public async Task TC_CX_10_the_first_turn_starts_the_thread_with_the_FR_33_7_parameters_and_the_FR_33_9_config()
	{
		var node = _f.Node();

		var sink = await _f.RunAsync(node, "Hello");

		var start = Assert.Single(_f.Requests("thread/start"));
		Assert.Equal(CodexEngineFixture.WorkingDirectory, start.GetProperty("cwd").GetString());
		Assert.Equal("gpt-6.2-sol", start.GetProperty("model").GetString());
		Assert.Equal("never", start.GetProperty("approvalPolicy").GetString());
		Assert.Equal("danger-full-access", start.GetProperty("sandbox").GetString());
		Assert.Equal("tessyn", start.GetProperty("serviceName").GetString());
		Assert.Equal("tessyn", start.GetProperty("threadSource").GetString());
		Assert.False(start.TryGetProperty("developerInstructions", out _));

		var config = start.GetProperty("config");
		var cmx = config.GetProperty("mcp_servers").GetProperty("cmx");
		Assert.Equal($"http://127.0.0.1:{_f.Mcp.Port}/mcp", cmx.GetProperty("url").GetString());
		Assert.Equal(node.AgentToken, cmx.GetProperty("http_headers").GetProperty("X-CMX-Token").GetString());
		Assert.Equal(20, cmx.GetProperty("startup_timeout_sec").GetInt32());
		Assert.Equal(3600, cmx.GetProperty("tool_timeout_sec").GetInt32());
		Assert.Equal("approve", cmx.GetProperty("default_tools_approval_mode").GetString());
		Assert.Equal(["CLAUDE.md"], config.GetProperty("project_doc_fallback_filenames").EnumerateArray().Select(e => e.GetString()));
		Assert.False(config.GetProperty("features.apps").GetBoolean());

		Assert.NotNull(node.CodexThreadId);
		Assert.Equal(node.CodexThreadId, node.ExternalId);
		Assert.Contains(node.CodexThreadId!, File.ReadAllText(Path.Combine(_f.Dir, "appsettings.json")));
		Assert.Equal("TurnEnded(success=True, error=, auth=False, limit=False, conversation=" + node.CodexThreadId + ", tokens=15600/50, cost=0)", sink.Calls.Last());
		Assert.Equal("low", _f.Requests("turn/start").Single().GetProperty("effort").GetString());
	}

	[Fact]
	public async Task TC_CX_10_the_thread_id_is_stored_and_saved_before_the_first_item_event_of_the_turn()
	{
		var node = _f.Node();
		string? storedAtTurnStart = null;
		string? savedAtTurnStart  = null;
		_f.Launcher.Configure = server => server.TurnScript = start =>
		{
			storedAtTurnStart = node.CodexThreadId;
			savedAtTurnStart  = File.ReadAllText(Path.Combine(_f.Dir, "appsettings.json"));
			return server.Scripts.TextTurn(start.ThreadId, start.TurnId, "Hi");
		};

		await _f.RunAsync(node);

		Assert.NotNull(storedAtTurnStart);
		Assert.Contains(storedAtTurnStart!, savedAtTurnStart);
	}

	[Fact]
	public async Task The_ExternalId_of_a_session_that_already_has_one_is_kept()
	{
		var node = _f.Node();
		node.ExternalId = "claude-session-1";

		await _f.RunAsync(node);

		Assert.Equal("claude-session-1", node.ExternalId);
		Assert.NotNull(node.CodexThreadId);
	}

	[Fact]
	public async Task TC_CX_12_later_turns_send_only_turn_start_and_every_turn_carries_the_model_and_effort_and_a_client_message_id()
	{
		var node = _f.Node();
		await _f.RunAsync(node, "one");
		await _f.RunAsync(node, "two", CodexEngineFixture.Chosen("gpt-6.1-terra", "high"));
		await _f.RunAsync(node, "three", CodexEngineFixture.Chosen("gpt-6.1-terra", "default"));

		Assert.Single(_f.Requests("thread/start"));
		Assert.Empty(_f.Requests("thread/resume"));
		var turns = _f.Requests("turn/start");
		Assert.Equal(3, turns.Length);
		Assert.All(turns, t => Assert.Equal(node.CodexThreadId, t.GetProperty("threadId").GetString()));
		Assert.Equal("gpt-6.2-sol", turns[0].GetProperty("model").GetString());
		Assert.Equal("low", turns[0].GetProperty("effort").GetString());
		Assert.Equal("gpt-6.1-terra", turns[1].GetProperty("model").GetString());
		Assert.Equal("high", turns[1].GetProperty("effort").GetString());
		Assert.False(turns[2].TryGetProperty("effort", out _)); // Default sends no effort (FR.33.8)
		Assert.All(turns, t => Assert.Equal("concise", t.GetProperty("summary").GetString()));
		Assert.Equal(3, turns.Select(t => t.GetProperty("clientUserMessageId").GetString()).Distinct().Count());
		Assert.Equal(["one", "two", "three"], turns.Select(CodexEngineFixture.InputText));
	}

	[Fact]
	public async Task TC_CX_12_the_first_turn_after_a_host_restart_resumes_the_thread_once_with_excludeTurns_and_its_config()
	{
		var node = _f.Node();
		await _f.RunAsync(node, "one");
		var threadId = node.CodexThreadId!;

		_f.Pool.TerminateAll();
		await _f.RunAsync(node, "two");
		await _f.RunAsync(node, "three");

		Assert.Equal(2, _f.Launcher.Servers.Count);
		var resume = Assert.Single(_f.Requests("thread/resume"));
		Assert.Equal(threadId, resume.GetProperty("threadId").GetString());
		Assert.True(resume.GetProperty("excludeTurns").GetBoolean());
		Assert.Equal(node.AgentToken, resume.GetProperty("config").GetProperty("mcp_servers").GetProperty("cmx").GetProperty("http_headers").GetProperty("X-CMX-Token").GetString());
		Assert.Equal(2, _f.Requests("turn/start").Length);
		Assert.Empty(_f.Requests("thread/start"));
		Assert.Equal(threadId, node.CodexThreadId);
	}

	[Fact]
	public async Task TC_CX_13_a_gone_thread_moves_to_the_prior_ids_and_a_new_thread_starts_with_the_context_preamble()
	{
		var node = _f.Node(threadId: "01a20000-0000-7000-8000-000000000000");
		_f.Files.Seed("USER", "What is the plan?");
		_f.Files.Seed("ASSISTANT", "Ship it on Friday.");
		_f.Files.Seed("USER", "And now?");
		_f.Launcher.Configure = server => server.GoneThreads["01a20000-0000-7000-8000-000000000000"] = 0;

		var sink = await _f.RunAsync(node, "And now?", augmented: "And now?\n---\nprefs");

		Assert.Equal(["01a20000-0000-7000-8000-000000000000"], node.PriorCodexThreadIds);
		Assert.NotNull(node.CodexThreadId);
		Assert.NotEqual("01a20000-0000-7000-8000-000000000000", node.CodexThreadId);
		Assert.Single(_f.Requests("thread/start"));
		var input = CodexEngineFixture.InputText(_f.Requests("turn/start").Single());
		Assert.StartsWith("The following is the conversation history", input);
		Assert.Contains("[Human]: What is the plan?", input);
		Assert.Contains("[Assistant]: Ship it on Friday.", input);
		Assert.EndsWith("And now?\n---\nprefs" + Environment.NewLine, input);
		Assert.Contains("Retrying(LostConversation)", sink.Calls);
		Assert.True(sink.Ends.Single().Success);
	}

	[Fact]
	public async Task TC_CX_13_an_active_writer_ends_the_turn_with_its_text_keeps_the_thread_and_does_not_reseed()
	{
		var node = _f.Node(threadId: "01a20229-aaaa-7000-8000-000000000001");
		_f.Launcher.Configure = server => server.ActiveWriterThreads["01a20229-aaaa-7000-8000-000000000001"] = 0;

		var sink = await _f.RunAsync(node);

		var end = sink.Ends.Single();
		Assert.False(end.Success);
		Assert.Equal("This conversation is open in another Codex window. Close it there and send again.", end.ErrorText);
		Assert.Equal("01a20229-aaaa-7000-8000-000000000001", node.CodexThreadId);
		Assert.Null(node.PriorCodexThreadIds);
		Assert.Empty(_f.Requests("thread/start"));
		Assert.Empty(_f.Requests("turn/start"));
	}

	[Fact]
	public async Task TC_CX_13_thread_not_found_on_turn_start_resumes_once_and_sends_the_turn_again()
	{
		var node = _f.Node();
		await _f.RunAsync(node, "one");
		_f.Server.Unload(node.CodexThreadId!);

		var sink = await _f.RunAsync(node, "two");

		Assert.Single(_f.Requests("thread/resume"));
		var turns = _f.Requests("turn/start");
		Assert.Equal(3, turns.Length); // one, the refused two, two again
		Assert.Equal("two", CodexEngineFixture.InputText(turns[2]));
		Assert.True(sink.Ends.Single().Success);
	}

	[Fact]
	public async Task TC_CX_14_the_scripted_tool_turn_maps_to_the_neutral_sink_calls_of_the_FR_33_11_table()
	{
		var node = _f.Node();
		_f.Launcher.Configure = server => server.TurnScript = start => server.Scripts.ToolTurn(start.ThreadId, start.TurnId);

		var sink = await _f.RunAsync(node);

		Assert.Equal(
		[
			"AssistantText(I'll run the three checks in order., limit=False)",
			"Usage(20653/215)",
			"ToolStarted(mcp-1, mcpToolCall, cmx.post_attention)",
			"ToolFinished(mcp-1, cmx.post_attention, error=False)",
			"ToolStarted(cmd-1, commandExecution, $ cmd /c echo spike-cmd-ok)",
			"ToolFinished(cmd-1, $ cmd /c echo spike-cmd-ok, error=False)",
			@"ToolStarted(fc-1, fileChange, edit 2 file(s): C:\Proj\Alpha\spike-edit.txt)",
			@"ToolFinished(fc-1, edit 2 file(s): C:\Proj\Alpha\spike-edit.txt, error=False)",
			"Progress(Plan: 1/3 - Run the command)",
			"Thinking(Checking the file)",
			"ToolStarted(cmd-2, commandExecution, $ Get-Content missing.txt)",
			"ToolFinished(cmd-2, $ Get-Content missing.txt (exit 1), error=True)",
			"ToolStarted(ws-1, webSearch, search \"codex app-server\")",
			"ToolFinished(ws-1, search \"codex app-server\", error=False)",
			@"ToolStarted(iv-1, imageView, view C:\Proj\Alpha\shot.png)",
			@"ToolFinished(iv-1, view C:\Proj\Alpha\shot.png, error=False)",
			"Progress(Codex: collabAgentToolCall)",
			"AssistantText(All three checks passed., limit=False)",
			"Usage(41653/335)",
			$"TurnEnded(success=True, error=, auth=False, limit=False, conversation={node.CodexThreadId}, tokens=41653/335, cost=0)",
		], sink.Calls);
	}

	[Fact]
	public async Task TC_CX_14_a_compaction_item_inside_a_turn_becomes_a_compaction_call()
	{
		var node = _f.Node();
		_f.Launcher.Configure = server => server.TurnScript = start =>
		[
			server.Scripts.TurnStarted(start.ThreadId, start.TurnId),
			server.Scripts.ItemCompleted(start.ThreadId, start.TurnId, new System.Text.Json.Nodes.JsonObject { ["type"] = "contextCompaction", ["id"] = "cc-9" }),
			.. server.Scripts.TextTurn(start.ThreadId, start.TurnId, "after").Skip(1),
		];

		var sink = await _f.RunAsync(node);

		Assert.Equal("Compacted", sink.Calls[0]);
		Assert.Contains("AssistantText(after, limit=False)", sink.Calls);
	}

	[Fact]
	public async Task TC_CX_15_the_worker_result_joins_the_agent_messages_with_a_blank_line_and_the_tokens_are_the_sums_of_last()
	{
		var node = _f.Node();
		_f.Launcher.Configure = server => server.TurnScript = start => server.Scripts.TextTurn(start.ThreadId, start.TurnId, "First part.", "Second part.");

		var sink = await _f.RunAsync(node);

		var end = sink.Ends.Single();
		Assert.True(end.Success);
		Assert.Equal("First part.\n\nSecond part.", end.ResultText);
		Assert.Equal(15600 + 15601, end.InputTokens);
		Assert.Equal(40 + 10 + 41 + 10, end.OutputTokens); // output includes the reasoning tokens
		Assert.Equal(0, end.CostUsd);
	}

	[Fact]
	public async Task A_failed_turn_ends_with_the_Codex_error_text_and_its_structured_kind()
	{
		var node = _f.Node();
		_f.Launcher.Configure = server => server.TurnScript = start =>
			server.Scripts.FailedTurn(start.ThreadId, start.TurnId, "usageLimitExceeded", "You've hit your usage limit.");

		var sink = await _f.RunAsync(node);

		var end = sink.Ends.Single();
		Assert.False(end.Success);
		Assert.Equal("Codex error: You've hit your usage limit. (usageLimitExceeded)", end.ErrorText);
		Assert.True(end.IsLimitError);
		Assert.False(end.IsAuthError);
	}

	[Fact]
	public async Task A_retrying_error_is_only_a_progress_line()
	{
		var node = _f.Node();
		_f.Launcher.Configure = server => server.TurnScript = start => server.Scripts.RetryThenCompleted(start.ThreadId, start.TurnId);

		var sink = await _f.RunAsync(node);

		Assert.Equal("Progress(Codex is retrying: Reconnecting... 1/5)", sink.Calls[0]);
		Assert.True(sink.Ends.Single().Success);
	}

	[Fact]
	public async Task The_thread_gets_the_session_name_after_the_first_turn_and_again_only_after_a_rename()
	{
		var node = _f.Node(name: "Release notes");
		await _f.RunAsync(node, "one");
		await _f.Eventually.TrueAsync(() => _f.Requests("thread/name/set").Length == 1, "the thread was named");
		await _f.RunAsync(node, "two");

		node.Name = "Release notes v2";
		await _f.RunAsync(node, "three");
		await _f.Eventually.TrueAsync(() => _f.Requests("thread/name/set").Length == 2, "the renamed thread was named again");

		var names = _f.Requests("thread/name/set");
		Assert.Equal(["Release notes", "Release notes v2"], names.Select(n => n.GetProperty("name").GetString()));
		Assert.All(names, n => Assert.Equal(node.CodexThreadId, n.GetProperty("threadId").GetString()));
	}
}
