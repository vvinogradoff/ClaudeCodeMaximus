using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Tests.Services.Engines;
using Xunit;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>
/// The minors of the phase 2b/3 review that were fixed (m1, m2, m4, m5, m7, m8, m9) and the two unverified points made safe (a thread pulled
/// away during a turn, a token in a Codex error text).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ReviewFixMinorTests : IAsyncLifetime
{
	private readonly CodexEngineFixture _f = new();

	public Task InitializeAsync() => Task.CompletedTask;

	public async Task DisposeAsync() => await _f.DisposeAsync();

	[Fact]
	public async Task m1_an_empty_model_list_keeps_the_cache_and_an_empty_catalog_does_not_count_as_loaded()
	{
		await using var s = new CodexServicesFixture();
		await s.LoadCatalogAsync();
		s.Launcher.Latest.ModelPages = [new JsonArray()];

		Assert.False(await s.Catalog.RefreshAsync());
		Assert.Equal(3, s.Catalog.Models.Count);

		await using var empty = new CodexServicesFixture();
		empty.WriteCache(empty.Clock.GetUtcNow());
		await empty.Catalog.StartAsync();
		Assert.True(empty.Catalog.IsLoaded);
		Assert.False(empty.Models.IsGptCatalogLoaded);
		Assert.Null(empty.Selector.SelectEngineId(new SessionNodeModel { Name = "G", FileName = "g.txt", EngineId = "codex" }, "gpt-x-sol"));
	}

	[Fact]
	public async Task m2_a_full_read_replaces_the_reading_and_only_updates_are_merged()
	{
		await using var s = new CodexServicesFixture();
		s.AddStoredProfile("codex_1", "u@example.com");
		var resets = s.Clock.GetUtcNow().AddHours(50).ToUnixTimeSeconds();
		JsonObject Window(double used, int minutes) => new() { ["usedPercent"] = used, ["windowDurationMins"] = minutes, ["resetsAt"] = resets };
		var snapshot = new JsonObject { ["limitId"] = "codex", ["primary"] = Window(10, 300), ["secondary"] = Window(20, 10080) };
		s.Launcher.Configure = server => server.On("account/rateLimits/read", call => call.ReplyAsync(new JsonObject
		{
			["rateLimits"] = snapshot.DeepClone(), ["rateLimitsByLimitId"] = new JsonObject { ["codex"] = snapshot.DeepClone() },
		}));
		await s.Usage.ReadAsync("codex_1");

		snapshot["primary"] = null; // a plan without the short window
		s.Clock.Advance(Constants.Remote.Usage.CacheDuration + TimeSpan.FromSeconds(1));
		var reading = await s.Usage.ReadAsync("codex_1");
		Assert.Null(reading!.Primary);
		Assert.Equal(("7d", 10080), (reading.Secondary!.Label, reading.Secondary.WindowDurationMins));

		var server = s.ServerOf(Path.Combine(s.StoredRoot, "codex_1"));
		await server.NotifyAsync("account/rateLimits/updated", new JsonObject { ["rateLimits"] = new JsonObject { ["limitId"] = "codex", ["rateLimitReachedType"] = "rateLimitReached" } });
		await s.Eventually.TrueAsync(() => s.Usage.GetReading("codex_1")?.RateLimitReachedType == "rateLimitReached", "the reached type arrived");
		await server.NotifyAsync("account/rateLimits/updated", new JsonObject { ["rateLimits"] = new JsonObject { ["limitId"] = "codex", ["credits"] = new JsonObject() } });
		await Task.Delay(100);
		Assert.Equal("rateLimitReached", s.Usage.GetReading("codex_1")!.RateLimitReachedType); // a null field keeps it
		await server.NotifyAsync("account/rateLimits/updated", new JsonObject { ["rateLimits"] = new JsonObject { ["limitId"] = "codex", ["secondary"] = Window(30, 10080) } });
		await s.Eventually.TrueAsync(() => s.Usage.GetReading("codex_1")?.RateLimitReachedType is null, "a reading with a window cleared it");
	}

	[Fact]
	public async Task m4_an_image_attachment_named_in_the_text_is_sent_as_localImage_and_a_text_only_model_gets_only_the_path()
	{
		var attachments = new FakeAttachmentService();
		attachments.Attachments.Add(new AttachmentDto("a1", @"C:\Data\attachments\n1\shot.png", "shot.png", 10, "image/png", DateTimeOffset.UtcNow));
		attachments.Attachments.Add(new AttachmentDto("a2", @"C:\Data\attachments\n1\notes.txt", "notes.txt", 10, "text/plain", DateTimeOffset.UtcNow));
		attachments.Attachments.Add(new AttachmentDto("a3", @"C:\Data\attachments\n1\other.png", "other.png", 10, "image/png", DateTimeOffset.UtcNow));
		await using var services = new CodexServicesFixture();
		services.WriteCache(services.Clock.GetUtcNow(), new CodexCatalogModel("gpt-text", "Text", true, [], null, ["text"], null), CodexServicesFixture.Cached("gpt-x-sol"));
		await services.Catalog.StartAsync();
		await using var f = new CodexEngineFixture(attachments: attachments, models: services.Models);
		var node = f.Node();
		var text = @"Look at C:\Data\attachments\n1\shot.png and C:\Data\attachments\n1\notes.txt";

		await f.RunAsync(node, text, CodexEngineFixture.Chosen("gpt-x-sol"));
		await f.RunAsync(node, text, CodexEngineFixture.Chosen("gpt-text"));

		var turns = f.Requests("turn/start");
		var withImages = turns[0].GetProperty("input").EnumerateArray().ToList();
		Assert.Equal(2, withImages.Count);
		Assert.Equal(text, withImages[0].GetProperty("text").GetString());
		Assert.Equal(("localImage", @"C:\Data\attachments\n1\shot.png"), (withImages[1].GetProperty("type").GetString(), withImages[1].GetProperty("path").GetString()));
		Assert.Single(turns[1].GetProperty("input").EnumerateArray());
	}

	[Fact]
	public async Task m5_clientUserMessageId_is_the_entry_id_of_the_turn_s_USER_entry()
	{
		await using var h = new CodexFullHarness();
		await h.InitializeAsync();
		var node = h.Node();

		await h.RunAsync(node, "Hello there");

		var index = h.H.Files.Entries.ToList().FindIndex(e => e.Role == "USER" && e.Content == "Hello there");
		Assert.Equal(EntryIds.Format(h.Entries.FileId, index), h.Requests("turn/start").Single().GetProperty("clientUserMessageId").GetString());
	}

	[Fact]
	public async Task m7_corrections_stay_queued_when_the_turn_fails_before_turn_start()
	{
		var node = _f.Node(threadId: "01a20229-aaaa-7000-8000-000000000009");
		_f.Launcher.Configure = server => server.ActiveWriterThreads["01a20229-aaaa-7000-8000-000000000009"] = 0;
		_f.Engine.SendCorrection(node, CodexEngineFixture.Chosen(), Constants.Instructions.MidRunAutoCommitOff);

		var refused = await _f.RunAsync(node, "one");
		Assert.False(refused.Ends.Single().Success);
		_f.Server.ActiveWriterThreads.Clear();
		await _f.RunAsync(node, "two");
		await _f.RunAsync(node, "three");

		var inputs = _f.Requests("turn/start").Select(CodexEngineFixture.InputText).ToList();
		Assert.Equal(["two\n- " + Constants.Instructions.MidRunAutoCommitOff, "three"], inputs);
	}

	[Fact]
	public async Task m8_a_thread_gone_during_the_not_loaded_retry_is_detached_and_reseeded()
	{
		var node = _f.Node();
		await _f.RunAsync(node, "first");
		var gone = node.CodexThreadId!;
		_f.Server.Unload(gone);
		_f.Server.GoneThreads[gone] = 0;
		_f.Files.Seed("USER", "first");

		var sink = await _f.RunAsync(node, "second");

		Assert.True(sink.Ends.Single().Success);
		Assert.Equal([gone], node.PriorCodexThreadIds);
		Assert.Equal(2, _f.Requests("thread/start").Length);
		Assert.StartsWith("The following is the conversation history", CodexEngineFixture.InputText(_f.Requests("turn/start").Last()));
		Assert.Contains("Retrying(LostConversation)", sink.Calls);
	}

	[Fact]
	public async Task m8_a_profile_home_mismatch_ends_the_turn_with_the_start_failed_text()
	{
		_f.Pool.GetHost(new CodexProfileRef(Constants.Codex.Profiles.DefaultProfileId, @"C:\elsewhere"));

		var sink = await _f.RunAsync(_f.Node());

		Assert.StartsWith("Codex could not start the turn: Codex profile codex-default already runs on another home folder.", sink.Ends.Single().ErrorText);
	}

	[Fact]
	public async Task m9_a_running_compaction_is_stopped_by_InterruptAsync_with_turn_interrupt()
	{
		var node = _f.Node();
		await _f.RunAsync(node);
		var thread = node.CodexThreadId!;
		_f.Server.On("thread/compact/start", async call =>
		{
			await call.ReplyEmptyAsync();
			await _f.Server.NotifyAsync("turn/started", new JsonObject { ["threadId"] = thread, ["turn"] = _f.Scripts.Turn("cmp-1", "inProgress") });
		});
		_f.Server.On("turn/interrupt", async call =>
		{
			await call.ReplyEmptyAsync();
			await _f.Server.NotifyAsync("turn/completed", new JsonObject { ["threadId"] = thread, ["turn"] = _f.Scripts.Turn("cmp-1", "interrupted") });
		});

		var compaction = _f.Engine.CompactConversationAsync(node, CancellationToken.None);
		await _f.Eventually.TrueAsync(() => _f.Requests("thread/compact/start").Length == 1, "the compaction started");
		await Task.Delay(100);

		Assert.True(await _f.Engine.InterruptAsync(node.NodeId));
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => compaction.WaitAsync(TimeSpan.FromSeconds(10)));
		Assert.Equal("cmp-1", _f.Requests("turn/interrupt").Single().GetProperty("turnId").GetString());
		Assert.Equal(thread, node.CodexThreadId);
	}

	[Fact]
	public async Task A_token_quoted_in_a_Codex_error_is_redacted_before_it_is_logged_or_written()
	{
		var node = _f.Node();
		_f.Launcher.Configure = server => server.On("thread/start", call =>
			call.ErrorAsync(-32600, $"failed to load configuration: invalid header value {node.AgentToken} in mcp_servers.cmx.http_headers"));

		var sink = await _f.RunAsync(node);

		var text = sink.Ends.Single().ErrorText!;
		Assert.DoesNotContain(node.AgentToken, text);
		Assert.Contains("<redacted>", text);
	}

	[Fact]
	public async Task A_thread_detached_while_its_turn_runs_is_unsubscribed_only_when_that_turn_ends()
	{
		var node = _f.Node();
		_f.Launcher.Configure = server => server.TurnScript = start => server.Scripts.StreamingThenHang(start.ThreadId, start.TurnId, "working");
		using var stop = new CancellationTokenSource();
		var turn = _f.RunAsync(node, ct: stop.Token, sink: new RecordingEngineSink());
		await _f.Eventually.TrueAsync(() => _f.Launcher.Servers.Count > 0 && _f.Requests("turn/start").Length == 1, "the turn started");
		var thread = node.CodexThreadId!;

		Assert.True(_f.Engine.DetachConversation(node));
		await Task.Delay(100);
		Assert.Empty(_f.Requests("thread/unsubscribe"));

		stop.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => turn);
		await _f.Eventually.TrueAsync(() => _f.Requests("thread/unsubscribe").Length == 1, "the detached thread was unsubscribed after its turn");
		Assert.Equal(thread, _f.Requests("thread/unsubscribe").Single().GetProperty("threadId").GetString());
	}
}
