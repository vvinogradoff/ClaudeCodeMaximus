using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Focus references, step 1 (docs/features/focus-references-attachments-previews.md sections 2.1 to 2.4): quote and file resolution,
/// storage and listing of refs, the <c>post_attention</c> tool, handle issue and <c>file.read</c>, the file-access policy and
/// <c>file.preview</c>, and <c>session.entries</c> with <c>aroundEntryId</c>, over real session files and a real content store.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusReferencesTests : IDisposable
{
	private readonly FocusRefsEnv _env = new();

	public void Dispose() => _env.Dispose();

	private static FocusRefRequest Quote(string quote, string? session = null, string? occurrence = null) =>
		new(Constants.Attention.RefKindEntry, Session: session, Quote: quote, Occurrence: occurrence);

	private AttentionRefModel Resolve(FocusRefRequest request, SessionNodeModel? caller = null) =>
		_env.Resolver.Resolve(caller ?? _env.Session, request);

	/// <summary>The content of the referenced entry as the daemon serves it; offset and length index into this text.</summary>
	private string ContentOf(AttentionRefModel reference)
	{
		Assert.True(EntryIds.TryParse(reference.EntryId, out var fileId, out var ordinal));
		return _env.Base.Reader.GetEntryById(_env.Session.FileName, fileId, ordinal).Content;
	}

	private static string Text(JsonNode result) => result["content"]![0]!["text"]!.GetValue<string>();

	// ── quote resolution ─────────────────────────────────────────────────────

	[Fact]
	public void An_exact_quote_resolves_to_the_entry_and_the_span_of_the_quote()
	{
		const string body = "I rewrote the parser.\nThe plan is: split the lexer first, then the parser.\nDone.";
		_env.WriteBodies("hello", body, "ok");

		var r = Resolve(Quote("split the lexer first"));

		Assert.Equal("entry", r.Kind);
		Assert.Equal(_env.Session.NodeId, r.NodeId);
		Assert.Equal(32, r.FileId!.Length);
		Assert.Equal(r.FileId + ":1", r.EntryId);
		Assert.Equal("split the lexer first", ContentOf(r).Substring(r.Offset!.Value, r.Length!.Value));
		Assert.Contains("split the lexer first", body);
	}

	[Fact]
	public void Whitespace_is_normalised_and_the_span_covers_the_original_text()
	{
		const string body = "intro\nthe   quick\n\n   brown\tfox jumps";
		_env.WriteBodies("q", body);

		var r = Resolve(Quote("the quick brown fox"));

		var span = ContentOf(r).Substring(r.Offset!.Value, r.Length!.Value);
		Assert.Matches(@"^the\s{3}quick\s+brown\tfox$", span);   // starts at "the", ends after "fox", whatever the line endings are
		Assert.Contains("the   quick", body);
	}

	[Fact]
	public void Matching_is_otherwise_exact_including_case()
	{
		_env.WriteBodies("q", "The Quick Brown Fox");

		var error = Assert.Throws<FocusRefException>(() => Resolve(Quote("the quick brown fox")));

		Assert.Contains("not found", error.Message);
	}

	[Fact]
	public void A_quote_that_is_not_in_the_session_fails_and_names_the_quote()
	{
		_env.WriteBodies("hello", "something else entirely");

		var error = Assert.Throws<FocusRefException>(() => Resolve(Quote("a phrase nobody wrote")));

		Assert.Contains("a phrase nobody wrote", error.Message);
		Assert.Contains("not found", error.Message);
	}

	[Fact]
	public void A_quote_that_occurs_twice_in_the_chosen_entry_is_ambiguous()
	{
		_env.WriteBodies("q", "run the tests, then run the tests again");

		var error = Assert.Throws<FocusRefException>(() => Resolve(Quote("run the tests")));

		Assert.Contains("2 times", error.Message);
		Assert.Contains("longer", error.Message);
	}

	[Fact]
	public void The_last_occurrence_is_the_default_and_first_picks_the_oldest_entry()
	{
		_env.WriteBodies("u0", "the build is green", "u2", "the build is green now", "u4");

		var byDefault = Resolve(Quote("the build is green"));
		var last      = Resolve(Quote("the build is green", occurrence: "last"));
		var first     = Resolve(Quote("the build is green", occurrence: "first"));

		Assert.EndsWith(":3", byDefault.EntryId);
		Assert.EndsWith(":3", last.EntryId);
		Assert.EndsWith(":1", first.EntryId);
	}

	[Fact]
	public void Occurrence_other_than_first_or_last_is_refused()
	{
		_env.WriteBodies("q", "text");

		Assert.Throws<FocusRefException>(() => Resolve(Quote("text", occurrence: "second")));
	}

	[Fact]
	public void A_quote_in_an_old_entry_is_found_past_the_search_page_size()
	{
		var bodies = new string[Constants.Attention.RefSearchPageEntries * 2 + 20];
		for (var i = 0; i < bodies.Length; i++)
			bodies[i] = $"filler {i}";
		bodies[5] = "the needle is here";
		_env.WriteBodies(bodies);

		var last  = Resolve(Quote("the needle is here"));
		var first = Resolve(Quote("the needle is here", occurrence: "first"));

		Assert.EndsWith(":5", last.EntryId);
		Assert.EndsWith(":5", first.EntryId);
	}

	[Fact]
	public void A_quote_can_point_into_another_session_by_prefix_or_full_id()
	{
		_env.WriteBodies("nothing relevant");
		var other = _env.AddSession("Other", "u", "the deployment finished at noon");

		var byFull   = Resolve(Quote("finished at noon", session: other.NodeId));
		var byPrefix = Resolve(Quote("finished at noon", session: other.NodeId[..Constants.Agent.NodeIdPrefixMinLength]));

		Assert.Equal(other.NodeId, byFull.NodeId);
		Assert.Equal(other.NodeId, byPrefix.NodeId);
		Assert.EndsWith(":1", byFull.EntryId);
	}

	[Fact]
	public void The_default_session_is_the_caller_not_another_one()
	{
		_env.WriteBodies("u", "only in the caller");
		_env.AddSession("Other", "u", "only in the other");

		Assert.Throws<FocusRefException>(() => Resolve(Quote("only in the other")));
		Assert.Equal(_env.Session.NodeId, Resolve(Quote("only in the caller")).NodeId);
	}

	[Fact]
	public void An_unknown_session_is_refused()
	{
		_env.WriteBodies("u", "text");

		var error = Assert.Throws<FocusRefException>(() => Resolve(Quote("text", session: "nonexistent-session-id")));

		Assert.Contains("nonexistent-session-id", error.Message);
	}

	[Fact]
	public void The_daemons_own_system_entries_are_not_searched()
	{
		_env.WriteBodies("u", "visible");
		_env.Base.Files.AppendMessage(_env.Session.FileName, Constants.SessionFile.RoleSystem, "[Attention A1 (ask)] only in a system entry");

		Assert.Throws<FocusRefException>(() => Resolve(Quote("only in a system entry")));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   \n  ")]
	public void A_blank_quote_is_refused(string quote)
	{
		_env.WriteBodies("u", "text");

		Assert.Throws<FocusRefException>(() => Resolve(Quote(quote)));
	}

	[Fact]
	public void A_quote_over_300_characters_is_refused()
	{
		_env.WriteBodies("u", new string('x', 400));

		var error = Assert.Throws<FocusRefException>(() => Resolve(Quote(new string('x', 301))));

		Assert.Contains("300", error.Message);
	}

	[Fact]
	public void An_empty_session_has_nothing_to_quote()
	{
		Assert.Throws<FocusRefException>(() => Resolve(Quote("anything")));
	}

	// ── file and url refs ────────────────────────────────────────────────────

	[Fact]
	public void A_relative_file_ref_resolves_against_the_working_directory_with_an_opaque_id()
	{
		var full = _env.WriteFile("docs/plan.md", "# Plan\n- one");

		var r = Resolve(new FocusRefRequest("file", Path: "docs/plan.md", Label: "the plan"));

		Assert.Equal("file", r.Kind);
		Assert.Matches("^[0-9a-f]{32}$", r.RefId);
		Assert.Equal("plan.md", r.Name);
		Assert.Equal("docs/plan.md", r.RelativePath);
		Assert.Equal(full, r.AbsolutePath);
		Assert.Equal(12, r.Size);
		Assert.Equal("text/markdown", r.Mime);
		Assert.Equal("the plan", r.Label);
		Assert.NotEqual(Resolve(new FocusRefRequest("file", Path: "docs/plan.md")).RefId, r.RefId);
	}

	[Fact]
	public void An_absolute_file_ref_outside_the_working_directory_has_no_relative_path()
	{
		var outside = Path.Combine(_env.Base.Root, "outside.txt");
		File.WriteAllText(outside, "x");

		var r = Resolve(new FocusRefRequest("file", Path: outside));

		Assert.Null(r.RelativePath);
		Assert.Equal(outside, r.AbsolutePath);
	}

	[Fact]
	public void A_file_that_does_not_exist_or_is_a_directory_is_refused()
	{
		Directory.CreateDirectory(Path.Combine(_env.WorkDir, "somedir"));

		Assert.Throws<FocusRefException>(() => Resolve(new FocusRefRequest("file", Path: "missing.md")));
		Assert.Throws<FocusRefException>(() => Resolve(new FocusRefRequest("file", Path: "somedir")));
	}

	[Fact]
	public void A_credential_file_cannot_be_referenced()
	{
		_env.WriteFile(".ssh/id_rsa", "secret");
		_env.WriteFile(".env", "KEY=1");

		var ssh = Assert.Throws<FocusRefException>(() => Resolve(new FocusRefRequest("file", Path: ".ssh/id_rsa")));
		Assert.Throws<FocusRefException>(() => Resolve(new FocusRefRequest("file", Path: ".env")));
		Assert.Contains("credential", ssh.Message);
	}

	[Theory]
	[InlineData("https://example.com/a?b=1", true)]
	[InlineData("http://example.com", true)]
	[InlineData("ftp://example.com", false)]
	[InlineData("javascript:alert(1)", false)]
	[InlineData("not a url", false)]
	public void Url_refs_are_http_or_https_only(string url, bool ok)
	{
		var request = new FocusRefRequest("url", Url: url, Label: "link");

		if (ok)
			Assert.Equal(url, Resolve(request).Url);
		else
			Assert.Throws<FocusRefException>(() => Resolve(request));
	}

	// ── the post_attention tool ──────────────────────────────────────────────

	[Fact]
	public async Task The_tool_schema_and_text_describe_refs()
	{
		var tools = ((JsonNode)typeof(AgentMcpServer)
			.GetMethod("HandleToolsList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null)!)["tools"]!
			.AsArray().ToDictionary(t => t!["name"]!.GetValue<string>(), t => t!);
		var post = tools["post_attention"];

		var refs = post["inputSchema"]!["properties"]!["items"]!["items"]!["properties"]!["refs"]!;
		Assert.Equal("array", refs["type"]!.GetValue<string>());
		Assert.Equal(10, refs["maxItems"]!.GetValue<int>());
		Assert.Equal(
			["kind", "label", "occurrence", "path", "quote", "session", "url"],
			refs["items"]!["properties"]!.AsObject().Select(p => p.Key).OrderBy(k => k).ToArray());
		Assert.Equal(["entry", "file", "url"], refs["items"]!["properties"]!["kind"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
		Assert.Equal(300, refs["items"]!["properties"]!["quote"]!["maxLength"]!.GetValue<int>());
		Assert.Equal("kind", refs["items"]!["required"]![0]!.GetValue<string>());

		var description = post["description"]!.GetValue<string>();
		Assert.Contains("`refs`", description);
		Assert.Contains("quote", description);
		Assert.Contains("file you wrote", description);
		Assert.Contains("Constants.Attention", "Constants.Attention");   // keeps the using honest when the text moves
		Assert.Contains("`refs`", Constants.Instructions.AttentionGuidance);
		await Task.CompletedTask;
	}

	[Fact]
	public async Task A_post_with_refs_stores_resolved_refs_on_the_item()
	{
		_env.WriteBodies("u", "Here is the migration plan: step one, then step two.");
		_env.WriteFile("plan.md", "# Plan");

		var result = await _env.PostAsync(FocusRefsEnv.ItemWithRefs(
			"Approve the plan",
			FocusRefsEnv.EntryRef("migration plan: step one", label: "the plan text"),
			FocusRefsEnv.FileRef("plan.md"),
			new JsonObject { ["kind"] = "url", ["url"] = "https://example.com/doc" }));

		Assert.Contains("A1 posted", Text(result));
		var refs = _env.Attention.GetItems(_env.Session.NodeId).Single().Refs;
		Assert.Equal(["entry", "file", "url"], refs.Select(r => r.Kind).ToArray());
		Assert.Equal("the plan text", refs[0].Label);
		Assert.EndsWith(":1", refs[0].EntryId);
		Assert.Equal("plan.md", refs[1].RelativePath);
		Assert.Equal("https://example.com/doc", refs[2].Url);
	}

	[Fact]
	public async Task An_unresolvable_ref_fails_the_whole_call_names_it_and_pins_nothing()
	{
		_env.WriteBodies("u", "some text");

		var ex = await Assert.ThrowsAnyAsync<Exception>(() => _env.PostAsync(FocusRefsEnv.ItemWithRefs(
			"Ask",
			FocusRefsEnv.EntryRef("some text"),
			FocusRefsEnv.EntryRef("this quote does not exist"))));

		Assert.EndsWith("McpToolException", ex.GetType().Name);
		Assert.Contains("items[0].refs[1]", ex.Message);
		Assert.Contains("this quote does not exist", ex.Message);
		Assert.Contains("post_attention again", ex.Message);
		Assert.Empty(_env.Attention.GetItems(_env.Session.NodeId));
	}

	[Fact]
	public async Task Malformed_refs_are_refused_with_the_item_and_ref_position()
	{
		_env.WriteBodies("u", "text");

		var notArray = new JsonObject { ["kind"] = "ask", ["title"] = "t", ["refs"] = "oops" };
		var tooMany  = FocusRefsEnv.ItemWithRefs("t", Enumerable.Range(0, 11).Select(_ => FocusRefsEnv.EntryRef("text")).ToArray());
		var noKind   = FocusRefsEnv.ItemWithRefs("t", new JsonObject { ["quote"] = "text" });

		Assert.Contains("'refs' must be an array", (await Assert.ThrowsAnyAsync<Exception>(() => _env.PostAsync(notArray))).Message);
		Assert.Contains("at most 10", (await Assert.ThrowsAnyAsync<Exception>(() => _env.PostAsync(tooMany))).Message);
		Assert.Contains("items[0].refs[0]", (await Assert.ThrowsAnyAsync<Exception>(() => _env.PostAsync(noKind))).Message);
	}

	[Fact]
	public async Task Reposting_an_open_item_replaces_refs_only_when_new_ones_are_given()
	{
		_env.WriteBodies("u", "alpha text", "beta text");
		await _env.PostAsync(FocusRefsEnv.ItemWithRefs("Same", FocusRefsEnv.EntryRef("alpha text")));
		var first = _env.Attention.GetItems(_env.Session.NodeId).Single().Refs.Single();

		await _env.PostAsync(new JsonObject { ["kind"] = "ask", ["title"] = "Same", ["body"] = "updated" });
		Assert.Equal(first.EntryId, _env.Attention.GetItems(_env.Session.NodeId).Single().Refs.Single().EntryId);

		await _env.PostAsync(FocusRefsEnv.ItemWithRefs("Same", FocusRefsEnv.EntryRef("beta text")));
		Assert.EndsWith(":2", _env.Attention.GetItems(_env.Session.NodeId).Single().Refs.Single().EntryId);
	}

	// ── persistence and listing ──────────────────────────────────────────────

	[Fact]
	public async Task Refs_survive_a_restart_and_are_listed_without_absolute_paths()
	{
		_env.WriteBodies("u", "the answer is forty two");
		var full = _env.WriteFile("out/report.md", "# Report");
		await _env.PostAsync(FocusRefsEnv.ItemWithRefs("Review", FocusRefsEnv.EntryRef("answer is forty two"), FocusRefsEnv.FileRef("out/report.md")));
		await _env.PostAsync(new JsonObject { ["kind"] = "alert", ["title"] = "No refs here" });

		_env.Restart();
		var list = await _env.ResultAsync("attention.list", _env.Base.NodeParams());

		var items = list["items"]!.AsArray();
		var withRefs = items.Single(i => i!["title"]!.GetValue<string>() == "Review")!["refs"]!.AsArray();
		Assert.Equal(2, withRefs.Count);
		Assert.Equal("entry", withRefs[0]!["kind"]!.GetValue<string>());
		Assert.Equal(_env.Session.NodeId, withRefs[0]!["nodeId"]!.GetValue<string>());
		Assert.EndsWith(":1", withRefs[0]!["entryId"]!.GetValue<string>());
		Assert.Equal("answer is forty two".Length, withRefs[0]!["length"]!.GetValue<int>());
		Assert.Equal("file", withRefs[1]!["kind"]!.GetValue<string>());
		Assert.Equal("report.md", withRefs[1]!["name"]!.GetValue<string>());
		Assert.Equal("out/report.md", withRefs[1]!["path"]!.GetValue<string>());
		Assert.Matches("^[0-9a-f]{32}$", withRefs[1]!["refId"]!.GetValue<string>());
		Assert.Equal(8, withRefs[1]!["size"]!.GetValue<long>());

		// No absolute path, and not even the temp folder, appears anywhere in the response.
		var json = list.ToJsonString();
		Assert.DoesNotContain(full, json.Replace("\\\\", "\\"));
		Assert.DoesNotContain(_env.Base.Root.Replace("\\", "\\\\"), json);
		Assert.DoesNotContain("absolutePath", json, StringComparison.OrdinalIgnoreCase);

		Assert.Null(items.Single(i => i!["title"]!.GetValue<string>() == "No refs here")!["refs"]);
	}

	[Fact]
	public void A_ledger_file_from_before_refs_is_still_readable()
	{
		Directory.CreateDirectory(_env.LedgerRoot);
		File.WriteAllText(Path.Combine(_env.LedgerRoot, _env.Session.NodeId + ".json"), """
			{ "NodeId": "x", "NextNumber": 3, "Items": [
			  { "Id": "A2", "Number": 2, "Kind": 1, "Title": "Old item", "Body": "b", "Options": [], "Status": 0,
			    "CreatedUtc": "2026-11-01T10:00:00+00:00", "UpdatedUtc": "2026-11-01T10:00:00+00:00" } ] }
			""");

		var items = new AttentionService(new AttentionLedgerStore(_env.LedgerRoot), _env.Base.Files).GetItems(_env.Session.NodeId);

		Assert.Equal("Old item", items.Single().Title);
		Assert.Empty(items.Single().Refs);
	}

	[Fact]
	public async Task The_client_dto_never_carries_the_absolute_path()
	{
		var full = _env.WriteFile("a.md", "x");
		await _env.PostAsync(FocusRefsEnv.ItemWithRefs("T", FocusRefsEnv.FileRef(full)));

		var dto = _env.Attention.GetItems(_env.Session.NodeId).Single().Refs.Single().ToDto();

		Assert.Equal("a.md", dto.Path);
		Assert.DoesNotContain(_env.Base.Root, System.Text.Json.JsonSerializer.Serialize(dto, TdcpJsonOptions.Default));
	}

	// ── handles and file.read ────────────────────────────────────────────────

	private async Task<string> PostFileRef(string relative)
	{
		await _env.PostAsync(FocusRefsEnv.ItemWithRefs("File " + relative, FocusRefsEnv.FileRef(relative)));
		return _env.Attention.GetItems(_env.Session.NodeId).First(i => i.Title == "File " + relative).Refs.Single().RefId!;
	}

	private static string ReadParams(string handle, long? offset = null, int? length = null) =>
		$"{{\"handle\":\"{handle}\"{(offset == null ? "" : ",\"offset\":" + offset)}{(length == null ? "" : ",\"length\":" + length)}}}";

	[Fact]
	public async Task A_text_file_is_read_whole_as_utf8_with_its_metadata()
	{
		_env.WriteBodies("u", "x");
		_env.WriteFile("notes.md", "# Title\nline two\n");
		var handle = await PostFileRef("notes.md");

		var r = await _env.ResultAsync("file.read", ReadParams(handle));

		Assert.Equal("notes.md", r["name"]!.GetValue<string>());
		Assert.Equal("text/markdown", r["mime"]!.GetValue<string>());
		Assert.Equal(17, r["size"]!.GetValue<long>());
		Assert.Equal(0, r["offset"]!.GetValue<long>());
		Assert.Equal(17, r["length"]!.GetValue<int>());
		Assert.Equal("utf8", r["encoding"]!.GetValue<string>());
		Assert.Equal("# Title\nline two\n", r["data"]!.GetValue<string>());
		Assert.True(r["eof"]!.GetValue<bool>());
		Assert.NotNull(r["mtimeUtc"]);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(5)]
	[InlineData(7)]
	public async Task Text_chunks_never_split_a_multibyte_character_and_add_up_to_the_file(int chunk)
	{
		const string text = "aé€😀b — ünïcödé ✓ plain ascii and 日本語 text 😀😀";
		_env.WriteFile("u.txt", text);
		var handle = await PostFileRef("u.txt");
		var total = Encoding.UTF8.GetByteCount(text);

		var sb = new StringBuilder();
		long offset = 0;
		var guard = 0;
		while (guard++ < 200)
		{
			var r = await _env.ResultAsync("file.read", ReadParams(handle, offset, chunk));
			Assert.Equal(offset, r["offset"]!.GetValue<long>());   // chunk boundaries are char-aligned, so no skipping needed here
			sb.Append(r["data"]!.GetValue<string>());
			Assert.DoesNotContain('\uFFFD', sb.ToString());
			offset += r["length"]!.GetValue<int>();
			if (r["eof"]!.GetValue<bool>())
				break;
		}

		Assert.Equal(text, sb.ToString());
		Assert.Equal(total, offset);
	}

	[Fact]
	public async Task A_text_read_starting_inside_a_character_moves_forward_and_reports_the_real_offset()
	{
		_env.WriteFile("e.txt", "é€x");   // bytes: C3 A9 E2 82 AC 78
		var handle = await PostFileRef("e.txt");

		var r = await _env.ResultAsync("file.read", ReadParams(handle, 1));   // byte 1 is the second byte of "é"

		Assert.Equal(2, r["offset"]!.GetValue<long>());
		Assert.Equal("€x", r["data"]!.GetValue<string>());
	}

	[Fact]
	public async Task A_binary_file_is_read_as_base64_in_chunks()
	{
		var bytes = Enumerable.Range(0, 1000).Select(i => (byte)(i * 7)).ToArray();
		_env.WriteFile("img.png", bytes);
		var handle = await PostFileRef("img.png");

		var collected = new MemoryStream();
		long offset = 0;
		JsonNode last;
		do
		{
			last = await _env.ResultAsync("file.read", ReadParams(handle, offset, 300));
			Assert.Equal("base64", last["encoding"]!.GetValue<string>());
			Assert.Equal("image/png", last["mime"]!.GetValue<string>());
			var chunk = Convert.FromBase64String(last["data"]!.GetValue<string>());
			Assert.Equal(last["length"]!.GetValue<int>(), chunk.Length);
			collected.Write(chunk);
			offset += chunk.Length;
		}
		while (!last["eof"]!.GetValue<bool>());

		Assert.Equal(bytes, collected.ToArray());
	}

	[Fact]
	public async Task Reading_past_the_end_returns_an_empty_chunk_at_eof()
	{
		_env.WriteFile("s.txt", "abc");
		var handle = await PostFileRef("s.txt");

		var r = await _env.ResultAsync("file.read", ReadParams(handle, 99));

		Assert.Equal(string.Empty, r["data"]!.GetValue<string>());
		Assert.True(r["eof"]!.GetValue<bool>());
	}

	[Fact]
	public async Task An_unknown_handle_a_negative_offset_and_a_zero_length_are_refused()
	{
		_env.WriteFile("s.txt", "abc");
		var handle = await PostFileRef("s.txt");

		Assert.Contains("Unknown file handle", await _env.ErrorAsync("file.read", ReadParams(new string('a', 32))));
		Assert.Contains("Unknown file handle", await _env.ErrorAsync("file.read", ReadParams("C:/Windows/win.ini")));
		await _env.ErrorAsync("file.read", ReadParams(handle, -1));
		await _env.ErrorAsync("file.read", ReadParams(handle, 0, 0));
		await _env.ErrorAsync("file.read", "{}");
	}

	[Fact]
	public async Task A_file_handle_still_works_after_a_daemon_restart()
	{
		_env.WriteFile("keep.md", "kept");
		var handle = await PostFileRef("keep.md");

		_env.Restart();
		var r = await _env.ResultAsync("file.read", ReadParams(handle));

		Assert.Equal("kept", r["data"]!.GetValue<string>());
	}

	[Fact]
	public async Task A_file_deleted_after_posting_reads_as_not_found()
	{
		var full = _env.WriteFile("gone.md", "x");
		var handle = await PostFileRef("gone.md");
		File.Delete(full);

		Assert.Contains("File not found", await _env.ErrorAsync("file.read", ReadParams(handle)));
	}

	// ── policy ───────────────────────────────────────────────────────────────

	[Theory]
	[InlineData(@"C:\Users\me\.ssh\id_rsa", true)]
	[InlineData(@"C:\Users\me\.ssh\config", true)]
	[InlineData(@"C:\Users\me\.aws\credentials", true)]
	[InlineData(@"C:\Users\me\.gnupg\pubring.kbx", true)]
	[InlineData(@"C:\proj\.env", true)]
	[InlineData(@"C:\proj\.env.production", true)]
	[InlineData(@"C:\proj\server.pem", true)]
	[InlineData(@"C:\proj\cert.pfx", true)]
	[InlineData(@"C:\Users\me\.claude\.credentials.json", true)]
	[InlineData(@"C:\proj\my-credentials.txt", true)]
	[InlineData(@"C:\Data\TessynDesktop\tdcp-token.dat", true)]
	[InlineData(@"C:\Data\TessynDesktop\web-relay.dat", true)]
	[InlineData(@"C:\Data\TessynDesktop\appsettings.json", true)]
	[InlineData(@"C:\Data\TessynDesktop\daemon.json", true)]
	[InlineData(@"C:\Data\TessynDesktop\mcp-tokens\x.json", true)]
	[InlineData(@"C:\Data\TessynDesktop\profiles\work\.claude.json", true)]
	[InlineData(@"C:\Data\TessynDesktop\sessions\a.txt", false)]
	[InlineData(@"C:\Data\TessynDesktop\attention\n.json", false)]
	[InlineData(@"C:\proj\docs\plan.md", false)]
	[InlineData(@"C:\proj\src\Program.cs", false)]
	public void Denied_paths(string path, bool denied)
	{
		Assert.Equal(denied, FileAccessPolicy.IsDenied(path, @"C:\Data\TessynDesktop"));
	}

	[Theory]
	[InlineData("a.md", FileTypeClass.Text)]
	[InlineData("a.TXT", FileTypeClass.Text)]
	[InlineData("a.cs", FileTypeClass.Text)]
	[InlineData("a.ts", FileTypeClass.Text)]
	[InlineData("a.json", FileTypeClass.Text)]
	[InlineData("a.png", FileTypeClass.Image)]
	[InlineData("a.jpg", FileTypeClass.Image)]
	[InlineData("a.webp", FileTypeClass.Image)]
	[InlineData("a.mp4", FileTypeClass.Video)]
	[InlineData("a.webm", FileTypeClass.Video)]
	[InlineData("a.mkv", FileTypeClass.Video)]
	[InlineData("a.avi", FileTypeClass.Video)]
	[InlineData("a.wmv", FileTypeClass.Video)]
	[InlineData("a.pdf", FileTypeClass.Pdf)]
	[InlineData("a.zip", FileTypeClass.Other)]
	[InlineData("a.exe", FileTypeClass.Other)]
	[InlineData("a", FileTypeClass.Other)]
	public void The_type_allowlist(string name, FileTypeClass expected)
	{
		Assert.Equal(expected, FileAccessPolicy.Classify(name).Class);
	}

	[Fact]
	public async Task The_relay_refuses_a_type_off_the_allowlist_but_loopback_reads_it()
	{
		_env.WriteFile("data.zip", [1, 2, 3, 4]);
		var handle = await PostFileRef("data.zip");

		var denied = await _env.ErrorAsync("file.read", ReadParams(handle), TdcpTransport.Relay);
		var ok = await _env.ResultAsync("file.read", ReadParams(handle), TdcpTransport.Loopback);

		Assert.Contains("cannot be read remotely", denied);
		Assert.Contains("allowlist", denied);
		Assert.Equal("AQIDBA==", ok["data"]!.GetValue<string>());
	}

	[Fact]
	public async Task The_relay_enforces_the_size_cap_of_the_type_and_loopback_does_not()
	{
		_env.WriteFile("big.txt", new string('x', 100));
		_env.Base.Settings.Settings.FileAccess.MaxTextFileBytes = 50;
		var handle = await PostFileRef("big.txt");

		Assert.Contains("larger than the 50 byte", await _env.ErrorAsync("file.read", ReadParams(handle), TdcpTransport.Relay));
		Assert.Equal(100, (await _env.ResultAsync("file.read", ReadParams(handle), TdcpTransport.Loopback))["data"]!.GetValue<string>().Length);
	}

	[Fact]
	public async Task Each_type_has_its_own_cap()
	{
		_env.WriteFile("a.png", new byte[20]);
		_env.WriteFile("a.mp4", new byte[20]);
		_env.WriteFile("a.pdf", new byte[20]);
		var png = await PostFileRef("a.png");
		var mp4 = await PostFileRef("a.mp4");
		var pdf = await PostFileRef("a.pdf");
		var settings = _env.Base.Settings.Settings.FileAccess;
		settings.MaxImageFileBytes = 10;
		settings.MaxVideoFileBytes = 100;
		settings.MaxPdfFileBytes = 100;

		await _env.ErrorAsync("file.read", ReadParams(png));
		await _env.ResultAsync("file.read", ReadParams(mp4));
		await _env.ResultAsync("file.read", ReadParams(pdf));
	}

	[Fact]
	public async Task The_relay_caps_the_chunk_and_loopback_gets_a_larger_one()
	{
		_env.WriteFile("c.txt", new string('y', 100));
		_env.Base.Settings.Settings.FileAccess.MaxRelayChunkBytes = 10;
		var handle = await PostFileRef("c.txt");

		var relay = await _env.ResultAsync("file.read", ReadParams(handle, null, 1000), TdcpTransport.Relay);
		var loopback = await _env.ResultAsync("file.read", ReadParams(handle, null, 1000), TdcpTransport.Loopback);

		Assert.Equal(10, relay["length"]!.GetValue<int>());
		Assert.False(relay["eof"]!.GetValue<bool>());
		Assert.Equal(100, loopback["length"]!.GetValue<int>());
		Assert.True(loopback["eof"]!.GetValue<bool>());
	}

	[Fact]
	public async Task A_denied_path_behind_a_handle_is_refused_even_when_the_ref_was_made_before_the_rule()
	{
		// A ledger from an earlier version may reference a path that the policy now denies: it is checked again on every read.
		var full = _env.WriteFile("k.txt", "x");
		var handle = await PostFileRef("k.txt");
		var item = _env.Attention.GetItems(_env.Session.NodeId).Single();
		Assert.Equal(full, item.Refs.Single().AbsolutePath);

		var denied = _env.WriteFile(".ssh/id_rsa", "secret");
		var ledger = _env.LedgerStore.Load(_env.Session.NodeId);
		ledger.Items[0].Refs[0].AbsolutePath = denied;
		_env.LedgerStore.Save(ledger);
		_env.Restart();

		Assert.Contains("credential", await _env.ErrorAsync("file.read", ReadParams(handle), TdcpTransport.Loopback));
		Assert.Contains("credential", await _env.ErrorAsync("file.read", ReadParams(handle), TdcpTransport.Relay));
	}

	[Fact]
	public async Task Every_relay_read_is_logged_and_so_is_a_refusal_but_loopback_reads_are_not()
	{
		_env.WriteFile("log.txt", "hello");
		_env.WriteFile("log.zip", [1]);
		var ok = await PostFileRef("log.txt");
		var bad = await PostFileRef("log.zip");

		await _env.ResultAsync("file.read", ReadParams(ok), TdcpTransport.Loopback);
		Assert.Empty(_env.Log.Messages());

		await _env.ResultAsync("file.read", ReadParams(ok), TdcpTransport.Relay);
		await _env.ErrorAsync("file.read", ReadParams(bad), TdcpTransport.Relay);

		var messages = _env.Log.Messages();
		Assert.Equal(2, messages.Length);
		Assert.Contains("Information: Relay file.read \"log.txt\"", messages[0]);
		Assert.Contains("Warning: Relay file.read refused", messages[1]);
	}

	[Fact]
	public void The_default_policy_is_the_working_directory_mode_decision_A11()
	{
		var settings = new AppSettingsModel().FileAccess;

		Assert.Equal(FileAccessMode.WorkingDirectory, settings.Mode);
		Assert.True(settings.MaxRelayChunkBytes > 0);
	}

	// ── file.preview ─────────────────────────────────────────────────────────

	private async Task<string> EntryIdOf(string quote)
	{
		var entries = await _env.ResultAsync("session.entries", _env.Base.NodeParams());
		return entries["entries"]!.AsArray().Last(e => e!["content"]!.GetValue<string>().Contains(quote))!["entryId"]!.GetValue<string>();
	}

	private string PreviewParams(string entryId, string path) =>
		new JsonObject { ["nodeId"] = _env.Session.NodeId, ["entryId"] = entryId, ["path"] = path }.ToJsonString();

	[Fact]
	public async Task A_path_that_occurs_in_the_entry_gets_a_handle_that_file_read_accepts()
	{
		var full = _env.WriteFile("out/result.md", "# Result");
		_env.WriteBodies("u", $"I wrote the report to {full} just now.");
		var entryId = await EntryIdOf("report");

		var preview = await _env.ResultAsync("file.preview", PreviewParams(entryId, full), TdcpTransport.Loopback);
		var read = await _env.ResultAsync("file.read", ReadParams(preview["handle"]!.GetValue<string>()), TdcpTransport.Loopback);

		Assert.Matches("^[0-9a-f]{32}$", preview["handle"]!.GetValue<string>());
		Assert.Equal("result.md", preview["name"]!.GetValue<string>());
		Assert.Equal("text/markdown", preview["mime"]!.GetValue<string>());
		Assert.Equal(8, preview["size"]!.GetValue<long>());
		Assert.Equal("# Result", read["data"]!.GetValue<string>());
	}

	[Fact]
	public async Task A_path_that_does_not_occur_in_the_entry_is_refused_even_for_an_existing_file()
	{
		var full = _env.WriteFile("other.md", "x");
		_env.WriteBodies("u", "No path in this entry at all.");
		var entryId = await EntryIdOf("No path");

		var loopback = await _env.ErrorAsync("file.preview", PreviewParams(entryId, full), TdcpTransport.Loopback);
		var relay = await _env.ErrorAsync("file.preview", PreviewParams(entryId, full), TdcpTransport.Relay);

		Assert.Contains("does not occur", loopback);
		Assert.Contains("does not occur", relay);
	}

	[Fact]
	public async Task The_path_may_be_written_with_either_slash_and_in_any_case()
	{
		var full = _env.WriteFile("Out/Result.md", "x");
		_env.WriteBodies("u", "written to " + full.Replace('\\', '/').ToUpperInvariant() + " ok");
		var entryId = await EntryIdOf("written");

		await _env.ResultAsync("file.preview", PreviewParams(entryId, full), TdcpTransport.Loopback);
	}

	[Fact]
	public async Task A_relative_path_in_the_entry_is_resolved_against_the_working_directory()
	{
		_env.WriteFile("docs/plan.md", "x");
		_env.WriteBodies("u", "see docs/plan.md for the plan");
		var entryId = await EntryIdOf("see docs");

		var preview = await _env.ResultAsync("file.preview", PreviewParams(entryId, "docs/plan.md"), TdcpTransport.Loopback);

		Assert.Equal("plan.md", preview["name"]!.GetValue<string>());
	}

	[Fact]
	public async Task A_relative_path_that_leaves_the_working_directory_is_refused()
	{
		_env.WriteBodies("u", @"see ..\..\outside.txt for it");
		var entryId = await EntryIdOf("see ..");

		var error = await _env.ErrorAsync("file.preview", PreviewParams(entryId, @"..\..\outside.txt"), TdcpTransport.Loopback);

		Assert.Contains("leaves the working directory", error);
	}

	[Fact]
	public async Task Preview_over_the_relay_in_the_referenced_only_mode_allows_only_referenced_files()
	{
		_env.Base.Settings.Settings.FileAccess.Mode = FileAccessMode.ReferencedOnly;
		var plain = _env.WriteFile("plain.md", "plain");
		var referenced = _env.WriteFile("ref.md", "referenced");
		_env.WriteBodies("u", $"files: {plain} and {referenced}");
		await PostFileRef("ref.md");
		var entryId = await EntryIdOf("files:");

		var denied = await _env.ErrorAsync("file.preview", PreviewParams(entryId, plain), TdcpTransport.Relay);
		var allowed = await _env.ResultAsync("file.preview", PreviewParams(entryId, referenced), TdcpTransport.Relay);
		var loopback = await _env.ResultAsync("file.preview", PreviewParams(entryId, plain), TdcpTransport.Loopback);

		Assert.Contains("Only files a Focus item references", denied);
		Assert.Equal("ref.md", allowed["name"]!.GetValue<string>());
		Assert.Equal("plain.md", loopback["name"]!.GetValue<string>());
	}

	[Fact]
	public async Task The_working_directory_mode_widens_the_relay_to_files_under_the_session_directory_only()
	{
		_env.Base.Settings.Settings.FileAccess.Mode = FileAccessMode.ReferencedOnly;
		var inside = _env.WriteFile("src/inside.cs", "class A {}");
		var outside = Path.Combine(_env.Base.Root, "outside.md");
		File.WriteAllText(outside, "x");
		_env.WriteBodies("u", $"look at {inside} and {outside}");
		var entryId = await EntryIdOf("look at");

		await _env.ErrorAsync("file.preview", PreviewParams(entryId, inside), TdcpTransport.Relay);
		_env.Base.Settings.Settings.FileAccess.Mode = FileAccessMode.WorkingDirectory;

		var preview = await _env.ResultAsync("file.preview", PreviewParams(entryId, inside), TdcpTransport.Relay);
		Assert.Equal("class A {}", (await _env.ResultAsync("file.read", ReadParams(preview["handle"]!.GetValue<string>()), TdcpTransport.Relay))["data"]!.GetValue<string>());
		await _env.ErrorAsync("file.preview", PreviewParams(entryId, outside), TdcpTransport.Relay);
	}

	[Fact]
	public async Task A_preview_handle_is_rechecked_when_the_mode_is_narrowed_again()
	{
		var inside = _env.WriteFile("n.md", "x");
		_env.WriteBodies("u", $"file {inside}");
		var entryId = await EntryIdOf("file ");
		_env.Base.Settings.Settings.FileAccess.Mode = FileAccessMode.WorkingDirectory;
		var handle = (await _env.ResultAsync("file.preview", PreviewParams(entryId, inside), TdcpTransport.Relay))["handle"]!.GetValue<string>();
		await _env.ResultAsync("file.read", ReadParams(handle), TdcpTransport.Relay);

		_env.Base.Settings.Settings.FileAccess.Mode = FileAccessMode.ReferencedOnly;

		await _env.ErrorAsync("file.read", ReadParams(handle), TdcpTransport.Relay);
	}

	[Fact]
	public async Task Preview_refuses_denied_files_missing_files_and_bad_ids()
	{
		var key = _env.WriteFile(".ssh/id_rsa", "secret");
		var env = _env.WriteFile(".env", "A=1");
		_env.WriteBodies("u", $"keys {key} {env} and {Path.Combine(_env.WorkDir, "nope.md")}");
		var entryId = await EntryIdOf("keys");

		Assert.Contains("credential", await _env.ErrorAsync("file.preview", PreviewParams(entryId, key), TdcpTransport.Loopback));
		Assert.Contains("credential", await _env.ErrorAsync("file.preview", PreviewParams(entryId, env), TdcpTransport.Loopback));
		Assert.Contains("File not found", await _env.ErrorAsync("file.preview", PreviewParams(entryId, Path.Combine(_env.WorkDir, "nope.md")), TdcpTransport.Loopback));
		await _env.ErrorAsync("file.preview", PreviewParams("not-an-entry-id", key), TdcpTransport.Loopback);
		await _env.ErrorAsync("file.preview", new JsonObject { ["nodeId"] = "nope", ["entryId"] = entryId, ["path"] = key }.ToJsonString(), TdcpTransport.Loopback);
	}

	// ── remote surface ───────────────────────────────────────────────────────

	[Fact]
	public void The_new_methods_are_remote_callable()
	{
		Assert.True(_env.Dispatcher.IsRemoteCallable("file.read"));
		Assert.True(_env.Dispatcher.IsRemoteCallable("file.preview"));
		Assert.Contains("file.read", _env.Dispatcher.RemoteMethodNames);
	}

	// ── session.entries aroundEntryId ────────────────────────────────────────

	private async Task<JsonNode> Around(string entryId, int? limit = null) =>
		await _env.ResultAsync("session.entries", _env.Base.NodeParams($"\"aroundEntryId\":\"{entryId}\"" + (limit == null ? "" : $",\"limit\":{limit}")));

	private static int[] Indexes(JsonNode page) => [.. page["entries"]!.AsArray().Select(e => e!["index"]!.GetValue<int>())];

	[Fact]
	public async Task The_page_is_centred_on_the_entry()
	{
		_env.Base.WriteConversation(40);
		var fileId = (await _env.ResultAsync("session.entries", _env.Base.NodeParams()))["fileId"]!.GetValue<string>();

		var page = await Around($"{fileId}:20", limit: 10);

		Assert.Equal(Enumerable.Range(15, 10), Indexes(page));
		Assert.Equal(fileId, page["fileId"]!.GetValue<string>());
		Assert.Equal(40, page["total"]!.GetValue<int>());
		Assert.Equal(40, page["seq"]!.GetValue<long>());
		Assert.Equal("m20", page["entries"]!.AsArray().Single(e => e!["index"]!.GetValue<int>() == 20)!["content"]!.GetValue<string>());
		Assert.Equal($"{fileId}:20", page["entries"]!.AsArray().Single(e => e!["index"]!.GetValue<int>() == 20)!["entryId"]!.GetValue<string>());
	}

	[Fact]
	public async Task Near_either_end_the_page_stays_full_and_contains_the_entry()
	{
		_env.Base.WriteConversation(40);
		var fileId = (await _env.ResultAsync("session.entries", _env.Base.NodeParams()))["fileId"]!.GetValue<string>();

		var first = await Around($"{fileId}:1", limit: 10);
		var last = await Around($"{fileId}:39", limit: 10);

		Assert.Equal(Enumerable.Range(0, 10), Indexes(first));
		Assert.Equal(Enumerable.Range(30, 10), Indexes(last));
	}

	[Fact]
	public async Task A_short_session_returns_everything_and_the_default_limit_applies()
	{
		_env.Base.WriteConversation(5);
		var fileId = (await _env.ResultAsync("session.entries", _env.Base.NodeParams()))["fileId"]!.GetValue<string>();

		Assert.Equal(Enumerable.Range(0, 5), Indexes(await Around($"{fileId}:2")));

		_env.Base.WriteConversation(120);
		fileId = (await _env.ResultAsync("session.entries", _env.Base.NodeParams()))["fileId"]!.GetValue<string>();
		var page = await Around($"{fileId}:60");
		Assert.Equal(Constants.Remote.Limits.EntriesDefaultLimit, page["entries"]!.AsArray().Count);
		Assert.Contains(60, Indexes(page));
	}

	[Fact]
	public async Task Entries_are_paged_on_from_an_around_page_with_the_existing_bounds()
	{
		_env.Base.WriteConversation(40);
		var fileId = (await _env.ResultAsync("session.entries", _env.Base.NodeParams()))["fileId"]!.GetValue<string>();
		var page = await Around($"{fileId}:20", limit: 10);

		var older = await _env.ResultAsync("session.entries", _env.Base.NodeParams($"\"beforeEntryId\":\"{fileId}:15\",\"limit\":5"));
		var newer = await _env.ResultAsync("session.entries", _env.Base.NodeParams($"\"afterSeq\":{Indexes(page).Max() + 1},\"fileId\":\"{fileId}\",\"limit\":5"));

		Assert.Equal(Enumerable.Range(10, 5), Indexes(older));
		Assert.Equal(Enumerable.Range(25, 5), Indexes(newer));
	}

	[Fact]
	public async Task Around_cannot_be_combined_and_rejects_unknown_stale_or_malformed_ids()
	{
		_env.Base.WriteConversation(10);
		var fileId = (await _env.ResultAsync("session.entries", _env.Base.NodeParams()))["fileId"]!.GetValue<string>();

		Assert.Contains("cannot be combined", await _env.ErrorAsync("session.entries", _env.Base.NodeParams($"\"aroundEntryId\":\"{fileId}:3\",\"beforeIndex\":5")));
		Assert.Contains("cannot be combined", await _env.ErrorAsync("session.entries", _env.Base.NodeParams($"\"aroundEntryId\":\"{fileId}:3\",\"beforeEntryId\":\"{fileId}:5\"")));
		Assert.Contains("Entry not found", await _env.ErrorAsync("session.entries", _env.Base.NodeParams($"\"aroundEntryId\":\"{fileId}:99\"")));
		Assert.Contains("not an entry id", await _env.ErrorAsync("session.entries", _env.Base.NodeParams("\"aroundEntryId\":\"nope\"")));
		Assert.Contains("history changed", await _env.ErrorAsync("session.entries", _env.Base.NodeParams($"\"aroundEntryId\":\"{new string('0', 32)}:3\"")));
	}

	[Fact]
	public async Task A_rewritten_file_makes_an_old_around_id_stale()
	{
		_env.Base.WriteConversation(10);
		var fileId = (await _env.ResultAsync("session.entries", _env.Base.NodeParams()))["fileId"]!.GetValue<string>();

		_env.Base.WriteConversation(8);   // a rewrite: new content generation
		_env.Base.Store.Invalidate(_env.Session.FileName);

		Assert.Contains("history changed", await _env.ErrorAsync("session.entries", _env.Base.NodeParams($"\"aroundEntryId\":\"{fileId}:3\"")));
	}

	[Fact]
	public async Task A_quote_ref_and_around_work_together_end_to_end()
	{
		_env.WriteBodies("u0", "a0", "u2", "the decisive sentence lives here", "u4", "a4", "u6", "a6");
		await _env.PostAsync(FocusRefsEnv.ItemWithRefs("Look at this", FocusRefsEnv.EntryRef("decisive sentence")));
		var reference = (await _env.ResultAsync("attention.list", _env.Base.NodeParams(), TdcpTransport.Loopback))["items"]![0]!["refs"]![0]!;

		var page = await Around(reference["entryId"]!.GetValue<string>(), limit: 4);
		var entry = page["entries"]!.AsArray().Single(e => e!["entryId"]!.GetValue<string>() == reference["entryId"]!.GetValue<string>())!;
		var content = entry["content"]!.GetValue<string>();

		Assert.Equal("decisive sentence", content.Substring(reference["offset"]!.GetValue<int>(), reference["length"]!.GetValue<int>()));
	}

	// ── end to end: the tool, the ledger, the wire ───────────────────────────

	[Fact]
	public async Task Post_attention_with_refs_shows_up_in_attention_list_over_loopback_and_the_relay()
	{
		_env.WriteBodies("u", "Findings: the cache is stale after deploy.");
		_env.WriteFile("plan.md", "# Plan\nrestart the cache");

		var posted = Text(await _env.PostAsync(FocusRefsEnv.ItemWithRefs(
			"Approve the plan",
			FocusRefsEnv.EntryRef("cache is stale after deploy", label: "finding"),
			FocusRefsEnv.FileRef("plan.md", label: "plan"))));
		Assert.Contains("A1 posted (ask)", posted);

		foreach (var transport in new[] { TdcpTransport.Loopback, TdcpTransport.Relay })
		{
			var list = await _env.ResultAsync("attention.list", _env.Base.NodeParams(), transport);
			var refs = list["items"]![0]!["refs"]!.AsArray();

			Assert.Equal(["entry", "file"], refs.Select(r => r!["kind"]!.GetValue<string>()).ToArray());
			Assert.Equal("finding", refs[0]!["label"]!.GetValue<string>());
			Assert.Equal("plan", refs[1]!["label"]!.GetValue<string>());
			Assert.Equal("plan.md", refs[1]!["path"]!.GetValue<string>());

			var read = await _env.ResultAsync("file.read", ReadParams(refs[1]!["refId"]!.GetValue<string>()), transport);
			Assert.Equal("# Plan\nrestart the cache", read["data"]!.GetValue<string>());
		}
	}
}
