using System;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services.Turns;
using Xunit;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>
/// Engine selection (FR.33.1, FR.33.4): the stored engine id picks Codex, the model-to-engine hook wins over it, and an engine change detaches
/// the other engine's conversation (stopping its live process first). Also the rename of a thread after a rename in Tessyn (FR.33.7).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexEngineRoutingTests : IAsyncLifetime
{
	private readonly CodexPipelineHarness _p = new();

	public Task InitializeAsync() => Task.CompletedTask;

	public async Task DisposeAsync() => await _p.DisposeAsync();

	/// <summary>The phase 3 hook, here a fixed answer.</summary>
	private sealed class FixedSelector(string? engineId) : ISessionEngineSelector
	{
		public string? SelectEngineId(SessionNodeModel node, string? modelId) => engineId;
	}

	[Fact]
	public void A_session_whose_engine_id_is_codex_runs_on_the_Codex_engine_and_null_or_claude_on_Claude()
	{
		var node = TurnEngineHarness.Node();

		Assert.Same(_p.Claude, _p.Router.For(node));
		node.EngineId = "CODEX";
		Assert.Same(_p.Codex, _p.Router.For(node));
		node.EngineId = "claude";
		Assert.Same(_p.Claude, _p.Router.For(node));
		Assert.Same(_p.Codex, _p.Router.Get("codex"));
	}

	[Fact]
	public void At_a_turn_the_model_to_engine_hook_wins_over_the_stored_id_stores_its_answer_and_null_keeps_it()
	{
		var node = TurnEngineHarness.Node();
		node.EngineId = "codex";

		Assert.Same(_p.Codex, new SessionEngineRegistry([_p.Claude, _p.Codex], selector: new FixedSelector(null)).ForTurn(node, "x"));
		Assert.Equal("codex", node.EngineId);
		Assert.Same(_p.Claude, new SessionEngineRegistry([_p.Claude, _p.Codex], selector: new FixedSelector("claude")).Peek(node, "x"));
		Assert.Equal("codex", node.EngineId); // Peek stores nothing
		Assert.Same(_p.Claude, new SessionEngineRegistry([_p.Claude, _p.Codex], selector: new FixedSelector("claude")).ForTurn(node, "x"));
		Assert.Null(node.EngineId); // Claude is stored as null, so a Claude session's settings stay as they were
		Assert.Same(_p.Codex, new SessionEngineRegistry([_p.Claude, _p.Codex], selector: new FixedSelector("codex")).ForTurn(node, "x"));
		Assert.Equal("codex", node.EngineId);
		Assert.Same(_p.Codex, new SessionEngineRegistry([_p.Claude, _p.Codex], selector: new FixedSelector("claude")).For(node)); // For reads the stored id
	}

	[Fact]
	public void An_engine_change_stops_a_live_Claude_streaming_process_before_it_detaches_the_Claude_session()
	{
		var node = TurnEngineHarness.Node(sessionId: "claude-live");
		_p.H.Settings.Settings.Tree.Add(new DirectoryNodeModel { Path = node.WorkingDirectory, Sessions = [node] });
		_p.H.Streaming.EnsureStarted(node.NodeId, null!, _ => { });

		var text = _p.Router.ApplyEngineChange(node, _p.Codex);

		Assert.Equal("Engine changed: Claude -> Codex (GPT). The conversation continues from the transcript.", text);
		Assert.Equal([node.NodeId], _p.H.Streaming.Stopped);
		Assert.Equal(["claude-live"], node.PriorClaudeSessionIds);
		Assert.Null(_p.Router.ApplyEngineChange(node, _p.Codex)); // nothing left to detach
	}

	[Fact]
	public async Task A_rename_in_Tessyn_renames_the_thread_while_its_host_runs()
	{
		var registry = new SessionRegistry(_p.H.Settings, _p.H.Files, new DirectoryLabelService(), _p.H.Status);
		await using var f = new CodexEngineFixture(registry: registry);
		var node = f.Node(name: "Before");
		await f.RunAsync(node);
		await f.Eventually.TrueAsync(() => f.Requests("thread/name/set").Length == 1, "the thread was named");

		registry.RenameSession(node, "After");

		await f.Eventually.TrueAsync(() => f.Requests("thread/name/set").Length == 2, "the rename reached the thread");
		Assert.Equal(["Before", "After"], f.Requests("thread/name/set").Select(r => r.GetProperty("name").GetString()));
	}
}
