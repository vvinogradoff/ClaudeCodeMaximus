using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// P1.F: stable entry ids, <c>fileId</c>/<c>seq</c>, <c>afterSeq</c>/<c>beforeEntryId</c> and <c>session.entry</c> by id with a range,
/// over a real session file and content store.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionEntryIdsTests : IDisposable
{
	private readonly EntryIdsEnv _env = new();

	public void Dispose() => _env.Dispose();

	private static string[] Contents(JsonNode result) =>
		[.. result["entries"]!.AsArray().Select(e => e!["content"]!.GetValue<string>())];

	private static string[] Ids(JsonNode result) =>
		[.. result["entries"]!.AsArray().Select(e => e!["entryId"]!.GetValue<string>())];

	// ── ids and seq on the existing calls ────────────────────────────────────────────────────

	[Fact]
	public async Task Entries_carry_an_entryId_and_the_result_a_fileId_and_seq()
	{
		_env.WriteConversation(5);

		var result = await _env.ResultAsync("session.entries", _env.NodeParams());

		var fileId = result["fileId"]!.GetValue<string>();
		Assert.Equal(32, fileId.Length);
		Assert.Equal(5, result["seq"]!.GetValue<long>());
		Assert.Equal(5, result["total"]!.GetValue<int>());
		Assert.Equal(Enumerable.Range(0, 5).Select(i => $"{fileId}:{i}"), Ids(result));
		Assert.Equal(Enumerable.Range(0, 5).Select(i => $"m{i}"), Contents(result));
	}

	[Fact]
	public async Task The_ids_of_session_entries_parse_with_EntryIds_and_match_index_and_fileId()
	{
		_env.WriteConversation(3);

		var result = await _env.ResultAsync("session.entries", _env.NodeParams());

		foreach (var entry in result["entries"]!.AsArray())
		{
			Assert.True(EntryIds.TryParse(entry!["entryId"]!.GetValue<string>(), out var fileId, out var ordinal));
			Assert.Equal(result["fileId"]!.GetValue<string>(), EntryIds.FormatFileId(fileId));
			Assert.Equal(entry["index"]!.GetValue<int>(), ordinal);
		}
	}

	[Fact]
	public async Task Session_get_reports_the_same_fileId_and_seq_as_the_entries_page()
	{
		_env.WriteConversation(4);

		var get     = await _env.ResultAsync("session.get", _env.NodeParams());
		var entries = await _env.ResultAsync("session.entries", _env.NodeParams());

		Assert.Equal(entries["fileId"]!.GetValue<string>(), get["fileId"]!.GetValue<string>());
		Assert.Equal(4, get["seq"]!.GetValue<long>());
		Assert.Equal(4, get["entryCount"]!.GetValue<int>());
	}

	[Fact]
	public async Task An_empty_or_missing_file_has_no_fileId_and_seq_zero()
	{
		_env.WriteRaw(string.Empty);
		var empty = await _env.ResultAsync("session.entries", _env.NodeParams());
		Assert.Equal(0, empty["seq"]!.GetValue<long>());
		Assert.Empty(empty["entries"]!.AsArray());

		System.IO.File.Delete(_env.FullPath);
		var missing = await _env.ResultAsync("session.entries", _env.NodeParams());
		Assert.Null(missing["fileId"]);
		Assert.Equal(0, missing["seq"]!.GetValue<long>());
	}

	// ── stability ────────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task Ids_survive_a_simulated_daemon_restart_and_new_entries_continue_the_ordinals()
	{
		_env.WriteConversation(6);
		var before = await _env.ResultAsync("session.entries", _env.NodeParams());

		_env.Restart();
		var after = await _env.ResultAsync("session.entries", _env.NodeParams());

		Assert.Equal(before["fileId"]!.GetValue<string>(), after["fileId"]!.GetValue<string>());
		Assert.Equal(Ids(before), Ids(after));

		// An id handed out before the restart still resolves, and appending continues the numbering without touching old ids.
		var oldId = Ids(before)[2];
		var entry = await _env.ResultAsync("session.entry", _env.NodeParams($"\"entryId\":\"{oldId}\""));
		Assert.Equal("m2", entry["content"]!.GetValue<string>());
		Assert.Equal(oldId, entry["entryId"]!.GetValue<string>());

		_env.AppendRaw(EntryIdsEnv.ConversationEntry(6));
		var grown = await _env.ResultAsync("session.entries", _env.NodeParams());
		Assert.Equal(7, grown["seq"]!.GetValue<long>());
		Assert.Equal(Ids(before), Ids(grown)[..6]);
		Assert.EndsWith(":6", Ids(grown)[6]);
	}

	[Fact]
	public async Task A_lost_sidecar_means_a_rebuild_with_a_new_fileId_never_a_silent_reuse_of_ordinals()
	{
		// A lost sidecar means a rebuild with a new fileId: the safe direction (clients reload), never a silent reuse of ordinals.
		_env.WriteConversation(3);
		var before = await _env.ResultAsync("session.entries", _env.NodeParams());
		_env.Store.Flush();
		_env.Store.Dispose();
		foreach (var file in System.IO.Directory.GetFiles(_env.IndexDirectory))
			System.IO.File.Delete(file);

		_env.Restart();
		var after = await _env.ResultAsync("session.entries", _env.NodeParams());

		Assert.NotEqual(before["fileId"]!.GetValue<string>(), after["fileId"]!.GetValue<string>());
		Assert.Equal(Contents(before), Contents(after));
	}

	[Fact]
	public async Task A_rewrite_gives_a_new_fileId_and_the_old_entry_id_is_rejected_as_history_changed()
	{
		_env.WriteConversation(4);
		var before = await _env.ResultAsync("session.entries", _env.NodeParams());
		var oldId  = Ids(before)[1];

		_env.Files.RewriteSessionFile(_env.FileName, EntryIdsEnv.ConversationEntry(0) + EntryIdsEnv.ConversationEntry(1));

		var after = await _env.ResultAsync("session.entries", _env.NodeParams());
		Assert.NotEqual(before["fileId"]!.GetValue<string>(), after["fileId"]!.GetValue<string>());

		var error = await _env.RawAsync("session.entry", _env.NodeParams($"\"entryId\":\"{oldId}\""));
		Assert.Equal(RemoteProtocol.ErrorCodes.DaemonError, EntryIdsEnv.ErrorCode(error));
		Assert.Equal("Session history changed", EntryIdsEnv.ErrorMessage(error));
	}

	// ── afterSeq / beforeEntryId ─────────────────────────────────────────────────────────────

	[Fact]
	public async Task AfterSeq_pages_forward_from_what_the_client_holds()
	{
		_env.WriteConversation(8);

		var first  = await _env.ResultAsync("session.entries", _env.NodeParams("\"afterSeq\":0,\"limit\":3"));
		var second = await _env.ResultAsync("session.entries", _env.NodeParams("\"afterSeq\":3,\"limit\":3"));
		var third  = await _env.ResultAsync("session.entries", _env.NodeParams("\"afterSeq\":6,\"limit\":3"));
		var none   = await _env.ResultAsync("session.entries", _env.NodeParams("\"afterSeq\":8,\"limit\":3"));
		var beyond = await _env.ResultAsync("session.entries", _env.NodeParams("\"afterSeq\":500"));

		Assert.Equal(["m0", "m1", "m2"], Contents(first));
		Assert.Equal(["m3", "m4", "m5"], Contents(second));
		Assert.Equal(["m6", "m7"], Contents(third));
		Assert.Empty(none["entries"]!.AsArray());
		Assert.Empty(beyond["entries"]!.AsArray());
		Assert.Equal(8, none["seq"]!.GetValue<long>());
	}

	[Fact]
	public async Task AfterSeq_equals_afterIndex_of_seq_minus_one()
	{
		_env.WriteConversation(7);

		var bySeq   = await _env.ResultAsync("session.entries", _env.NodeParams("\"afterSeq\":4"));
		var byIndex = await _env.ResultAsync("session.entries", _env.NodeParams("\"afterIndex\":3"));

		Assert.Equal(EntryIdsEnv.Canonical(byIndex), EntryIdsEnv.Canonical(bySeq));
	}

	[Fact]
	public async Task AfterSeq_with_the_current_fileId_works_and_with_a_stale_one_is_history_changed()
	{
		_env.WriteConversation(5);
		var fileId = (await _env.ResultAsync("session.entries", _env.NodeParams()))["fileId"]!.GetValue<string>();

		var ok = await _env.ResultAsync("session.entries", _env.NodeParams($"\"afterSeq\":3,\"fileId\":\"{fileId}\""));
		Assert.Equal(["m3", "m4"], Contents(ok));

		var stale = await _env.RawAsync("session.entries", _env.NodeParams($"\"afterSeq\":3,\"fileId\":\"{new string('a', 32)}\""));
		Assert.Equal(RemoteProtocol.ErrorCodes.DaemonError, EntryIdsEnv.ErrorCode(stale));
		Assert.Equal("Session history changed", EntryIdsEnv.ErrorMessage(stale));
	}

	[Fact]
	public async Task BeforeEntryId_pages_backward()
	{
		_env.WriteConversation(9);
		var ids = Ids(await _env.ResultAsync("session.entries", _env.NodeParams("\"limit\":200")));

		var page1 = await _env.ResultAsync("session.entries", _env.NodeParams($"\"beforeEntryId\":\"{ids[9 - 1]}\",\"limit\":3"));
		var page2 = await _env.ResultAsync("session.entries", _env.NodeParams($"\"beforeEntryId\":\"{Ids(page1)[0]}\",\"limit\":3"));
		var page3 = await _env.ResultAsync("session.entries", _env.NodeParams($"\"beforeEntryId\":\"{Ids(page2)[0]}\",\"limit\":3"));

		Assert.Equal(["m5", "m6", "m7"], Contents(page1));
		Assert.Equal(["m2", "m3", "m4"], Contents(page2));
		Assert.Equal(["m0", "m1"], Contents(page3));
	}

	[Fact]
	public async Task BeforeEntryId_equals_beforeIndex_of_its_ordinal()
	{
		_env.WriteConversation(9);
		var ids = Ids(await _env.ResultAsync("session.entries", _env.NodeParams("\"limit\":200")));

		var byId    = await _env.ResultAsync("session.entries", _env.NodeParams($"\"beforeEntryId\":\"{ids[6]}\",\"limit\":4"));
		var byIndex = await _env.ResultAsync("session.entries", _env.NodeParams("\"beforeIndex\":6,\"limit\":4"));

		Assert.Equal(EntryIdsEnv.Canonical(byIndex), EntryIdsEnv.Canonical(byId));
	}

	[Theory]
	[InlineData("\"afterSeq\":1,\"afterIndex\":1")]
	[InlineData("\"afterSeq\":1,\"beforeIndex\":3")]
	[InlineData("\"afterSeq\":1,\"beforeEntryId\":\"x\"")]
	[InlineData("\"beforeEntryId\":\"x\",\"beforeIndex\":3")]
	[InlineData("\"afterSeq\":-1")]
	[InlineData("\"beforeEntryId\":\"nonsense\"")]
	[InlineData("\"afterSeq\":1,\"fileId\":\"nonsense\"")]
	[InlineData("\"afterIndex\":1,\"fileId\":\"00000000000000000000000000000000\"")]
	[InlineData("\"afterSeq\":\"one\"")]
	public async Task Invalid_bound_combinations_and_values_are_invalid_params(string extra)
	{
		_env.WriteConversation(3);

		var response = await _env.RawAsync("session.entries", _env.NodeParams(extra));

		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, EntryIdsEnv.ErrorCode(response));
	}

	// ── session.entry by id and range ────────────────────────────────────────────────────────

	[Fact]
	public async Task Entry_by_id_returns_the_full_entry_with_its_id()
	{
		_env.WriteConversation(4);
		var ids = Ids(await _env.ResultAsync("session.entries", _env.NodeParams()));

		var entry = await _env.ResultAsync("session.entry", _env.NodeParams($"\"entryId\":\"{ids[3]}\""));

		Assert.Equal("m3", entry["content"]!.GetValue<string>());
		Assert.Equal("assistant", entry["role"]!.GetValue<string>());
		Assert.Equal(3, entry["index"]!.GetValue<int>());
		Assert.Equal(ids[3], entry["entryId"]!.GetValue<string>());
	}

	[Fact]
	public async Task Entry_by_id_errors_for_a_missing_ordinal_and_bad_ids_and_mixed_forms()
	{
		_env.WriteConversation(2);
		var fileId = (await _env.ResultAsync("session.entries", _env.NodeParams()))["fileId"]!.GetValue<string>();

		var missing = await _env.RawAsync("session.entry", _env.NodeParams($"\"entryId\":\"{fileId}:9\""));
		Assert.Equal(RemoteProtocol.ErrorCodes.DaemonError, EntryIdsEnv.ErrorCode(missing));
		Assert.Equal("Entry not found", EntryIdsEnv.ErrorMessage(missing));

		var malformed = await _env.RawAsync("session.entry", _env.NodeParams("\"entryId\":\"nonsense\""));
		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, EntryIdsEnv.ErrorCode(malformed));

		var mixed = await _env.RawAsync("session.entry", _env.NodeParams($"\"entryId\":\"{fileId}:0\",\"index\":0,\"epoch\":0"));
		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, EntryIdsEnv.ErrorCode(mixed));
	}

	[Fact]
	public async Task The_legacy_index_and_epoch_form_of_session_entry_still_works_and_now_also_carries_the_id()
	{
		_env.WriteConversation(3);
		var page = await _env.ResultAsync("session.entries", _env.NodeParams());

		var entry = await _env.ResultAsync("session.entry", _env.NodeParams($"\"index\":1,\"epoch\":{page["epoch"]!.GetValue<int>()}"));

		Assert.Equal("m1", entry["content"]!.GetValue<string>());
		Assert.Equal(Ids(page)[1], entry["entryId"]!.GetValue<string>());
	}

	[Fact]
	public async Task A_content_range_returns_a_slice_that_can_be_continued_to_rebuild_the_entry()
	{
		var body = string.Concat(Enumerable.Range(0, 400).Select(i => $"line{i:D3}|"));
		_env.WriteRaw(EntryIdsEnv.EntryText("ASSISTANT", 0, body));
		var id = Ids(await _env.ResultAsync("session.entries", _env.NodeParams()))[0];

		var sb     = new System.Text.StringBuilder();
		var offset = 0;
		var calls  = 0;
		while (true)
		{
			var slice = await _env.ResultAsync("session.entry", _env.NodeParams($"\"entryId\":\"{id}\",\"range\":{{\"offset\":{offset},\"length\":1000}}"));
			calls++;
			Assert.Equal(offset, slice["contentOffset"]!.GetValue<int>());
			Assert.Equal(body.Length, slice["fullLength"]!.GetValue<int>());
			var text = slice["content"]!.GetValue<string>();
			sb.Append(text);
			offset += text.Length;
			if (offset >= body.Length)
				break;

			Assert.True(slice["truncated"]!.GetValue<bool>());
		}

		Assert.Equal(body, sb.ToString());
		Assert.Equal(4, calls);
	}

	[Fact]
	public async Task A_range_never_splits_a_surrogate_pair_and_continuation_loses_nothing()
	{
		var body = string.Concat(Enumerable.Repeat("a\U0001F600", 50)); // 150 chars, every second pair straddles odd offsets
		_env.WriteRaw(EntryIdsEnv.EntryText("USER", 0, body));
		var id = Ids(await _env.ResultAsync("session.entries", _env.NodeParams()))[0];

		var sb     = new System.Text.StringBuilder();
		var offset = 0;
		while (offset < body.Length)
		{
			var slice = await _env.ResultAsync("session.entry", _env.NodeParams($"\"entryId\":\"{id}\",\"range\":{{\"offset\":{offset},\"length\":2}}"));
			var text  = slice["content"]!.GetValue<string>();
			var start = slice["contentOffset"]!.GetValue<int>();

			Assert.False(text.Length > 0 && char.IsLowSurrogate(text[0]));
			Assert.False(text.Length > 0 && char.IsHighSurrogate(text[^1]));
			Assert.True(text.Length > 0);

			sb.Append(text[(offset - start)..]);
			offset = start + text.Length;
		}

		Assert.Equal(body, sb.ToString());
	}

	[Fact]
	public async Task A_range_past_the_end_is_empty_and_bad_ranges_are_invalid_params()
	{
		_env.WriteConversation(1);
		var id = Ids(await _env.ResultAsync("session.entries", _env.NodeParams()))[0];

		var past = await _env.ResultAsync("session.entry", _env.NodeParams($"\"entryId\":\"{id}\",\"range\":{{\"offset\":100}}"));
		Assert.Equal(string.Empty, past["content"]!.GetValue<string>());
		Assert.Equal(2, past["fullLength"]!.GetValue<int>());

		foreach (var range in new[] { "{\"offset\":-1}", "{\"length\":0}", "\"x\"", "{\"offset\":\"a\"}" })
		{
			var response = await _env.RawAsync("session.entry", _env.NodeParams($"\"entryId\":\"{id}\",\"range\":{range}"));
			Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, EntryIdsEnv.ErrorCode(response));
		}
	}

	[Fact]
	public async Task A_range_also_works_with_the_legacy_index_and_epoch_form()
	{
		_env.WriteRaw(EntryIdsEnv.EntryText("USER", 0, "0123456789"));
		var page = await _env.ResultAsync("session.entries", _env.NodeParams());

		var slice = await _env.ResultAsync("session.entry", _env.NodeParams($"\"index\":0,\"epoch\":{page["epoch"]!.GetValue<int>()},\"range\":{{\"offset\":2,\"length\":3}}"));

		Assert.Equal("234", slice["content"]!.GetValue<string>());
		Assert.Equal(2, slice["contentOffset"]!.GetValue<int>());
		Assert.Equal(10, slice["fullLength"]!.GetValue<int>());
		Assert.True(slice["truncated"]!.GetValue<bool>());
	}

	// ── without a store ──────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task Without_a_content_store_the_new_fields_are_absent_and_id_params_are_invalid()
	{
		_env.WriteConversation(3);

		var page = await _env.LegacyResultAsync("session.entries", _env.NodeParams());
		Assert.Null(page["fileId"]);
		Assert.Null(page["seq"]);
		Assert.All(page["entries"]!.AsArray(), e => Assert.Null(e!["entryId"]));

		// afterSeq works without a store (it is afterIndex of seq - 1) ...
		var afterSeq = await _env.LegacyResultAsync("session.entries", _env.NodeParams("\"afterSeq\":2"));
		Assert.Equal(["m2"], Contents(afterSeq));

		// ... but ids need one.
		var byId = await _env.LegacyRawAsync("session.entry", _env.NodeParams($"\"entryId\":\"{new string('a', 32)}:0\""));
		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, EntryIdsEnv.ErrorCode(byId));
	}

	// ── EntryIds ─────────────────────────────────────────────────────────────────────────────

	[Fact]
	public void EntryIds_round_trip_and_reject_malformed_text()
	{
		var fileId = Guid.NewGuid();
		var text   = EntryIds.Format(fileId, 1234);

		Assert.Equal($"{fileId:N}:1234", text);
		Assert.True(EntryIds.TryParse(text, out var parsedFile, out var parsedOrdinal));
		Assert.Equal(fileId, parsedFile);
		Assert.Equal(1234, parsedOrdinal);

		foreach (var bad in new[] { null, "", "nonsense", $"{fileId:N}", $"{fileId:N}:", $"{fileId:N}:-1", $"{fileId:N}:1x", $"{fileId:D}:1", $"{fileId:N}-1", $"{fileId:N}:99999999999" })
			Assert.False(EntryIds.TryParse(bad, out _, out _), bad);

		Assert.True(EntryIds.TryParseFileId(EntryIds.FormatFileId(fileId), out var file));
		Assert.Equal(fileId, file);
		Assert.False(EntryIds.TryParseFileId(fileId.ToString("D"), out _));
	}
}
