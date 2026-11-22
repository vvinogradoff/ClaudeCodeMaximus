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
	public async Task At_a_turn_the_catalog_backed_selector_decides_and_stores_the_engine_and_keeps_a_GPT_session_while_the_catalog_is_unknown()
	{
		await using var s = new CodexServicesFixture();
		var router = new SessionEngineRegistry([_p.Claude, _p.Codex], selector: s.Selector);
		var node = TurnEngineHarness.Node();
		node.EngineId = "codex";

		// Never loaded: a GPT-looking model keeps the stored engine (FR.33.4 exception); a Claude model is Claude.
		Assert.Same(_p.Codex, router.ForTurn(node, "gpt-x-sol"));
		Assert.Equal("codex", node.EngineId);
		Assert.Same(_p.Claude, router.Peek(node, FixedClaudeModels.Opus));
		Assert.Equal("codex", node.EngineId); // Peek stores nothing

		// Loaded but empty: still unknown.
		s.WriteCache(s.Clock.GetUtcNow());
		await s.Catalog.StartAsync();
		Assert.Same(_p.Codex, router.ForTurn(node, "gpt-x-sol"));

		// Loaded: the catalog decides. Claude is stored as null, so a Claude session's settings stay as they were.
		await s.LoadCatalogAsync();
		Assert.Same(_p.Claude, router.ForTurn(node, "gpt-old-1"));
		Assert.Null(node.EngineId);
		Assert.Same(_p.Codex, router.ForTurn(node, "gpt-x-sol"));
		Assert.Equal("codex", node.EngineId);
		Assert.Same(_p.Codex, router.For(node)); // For reads the stored id
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
