using System;
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
/// P1.G (O(1) <c>session.get</c>, loopback <c>session.headers</c>) and the proof that the web contract still holds: calls
/// without the new params answer exactly what the pre-P1.F module answered, plus only the new optional fields.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionHeadersAndCompatibilityTests : IDisposable
{
	/// <summary>Every field P1.F and P1.G added to existing results.</summary>
	private static readonly string[] NewFields = ["entryId", "fileId", "seq", "contentOffset"];

	private readonly EntryIdsEnv _env = new();

	public void Dispose() => _env.Dispose();

	private static string Mixed()
	{
		var sb = new StringBuilder();
		sb.Append(EntryIdsEnv.EntryText("USER", 0, "first", "profile=\"work\""));
		sb.Append(EntryIdsEnv.EntryText("ASSISTANT", 1, "reply [with] brackets", "model=\"opus\" effort=\"high\""));
		sb.Append(EntryIdsEnv.EntryText("USER", 2, "[x] a checklist line\n\n[link](http://x)", "profile=\"other\""));
		sb.Append(EntryIdsEnv.EntryText("ASSISTANT", 3, "no model here"));
		sb.Append(EntryIdsEnv.EntryText("ASSISTANT", 4, new string('z', Constants.Remote.Limits.EntryContentMaxChars + 100), "model=\"sonnet\""));
		sb.Append(EntryIdsEnv.EntryText("SYSTEM", 5, "a system note", "profile=\"sys\" model=\"haiku\" effort=\"low\""));
		sb.Append(EntryIdsEnv.EntryText("USER", 6, "last user without profile"));
		return sb.ToString();
	}

	// ── responses without the new params are unchanged ───────────────────────────────────────

	[Theory]
	[InlineData("{}")]
	[InlineData("{\"limit\":3}")]
	[InlineData("{\"limit\":200}")]
	[InlineData("{\"afterIndex\":2,\"limit\":2}")]
	[InlineData("{\"afterIndex\":-1}")]
	[InlineData("{\"beforeIndex\":4,\"limit\":2}")]
	[InlineData("{\"beforeIndex\":0}")]
	[InlineData("{\"beforeIndex\":99}")]
	public async Task Session_entries_without_the_new_params_equals_the_legacy_answer_apart_from_the_new_fields(string extra)
	{
		_env.WriteRaw(Mixed());
		var paramsJson = extra == "{}" ? _env.NodeParams() : _env.NodeParams(extra.Trim('{', '}'));

		var current = await _env.ResultAsync("session.entries", paramsJson);
		var legacy  = await _env.LegacyResultAsync("session.entries", paramsJson);

		Assert.Equal(EntryIdsEnv.Canonical(legacy), EntryIdsEnv.Canonical(EntryIdsEnv.Without(current, NewFields)));
	}

	[Fact]
	public async Task Session_entries_keeps_the_dense_index_epoch_total_and_the_truncation_fields()
	{
		_env.WriteRaw(Mixed());

		var result = await _env.ResultAsync("session.entries", _env.NodeParams("\"limit\":200"));

		Assert.Equal(7, result["total"]!.GetValue<int>());
		Assert.Equal(0, result["epoch"]!.GetValue<int>());
		Assert.Equal(Enumerable.Range(0, 7), result["entries"]!.AsArray().Select(e => e!["index"]!.GetValue<int>()));
		var big = result["entries"]![4]!;
		Assert.True(big["truncated"]!.GetValue<bool>());
		Assert.Equal(Constants.Remote.Limits.EntryContentMaxChars + 100, big["fullLength"]!.GetValue<int>());
		Assert.Equal(Constants.Remote.Limits.EntryContentMaxChars, big["content"]!.GetValue<string>().Length);
		Assert.Null(result["entries"]![0]!["truncated"]);
		Assert.Null(result["entries"]![0]!["contentOffset"]);
	}

	[Fact]
	public async Task Session_entry_by_index_and_epoch_equals_the_legacy_answer_apart_from_the_new_fields()
	{
		_env.WriteRaw(Mixed());
		for (var index = 0; index < 7; index++)
		{
			var paramsJson = _env.NodeParams($"\"index\":{index},\"epoch\":0");

			var current = await _env.ResultAsync("session.entry", paramsJson);
			var legacy  = await _env.LegacyResultAsync("session.entry", paramsJson);

			Assert.Equal(EntryIdsEnv.Canonical(legacy), EntryIdsEnv.Canonical(EntryIdsEnv.Without(current, NewFields)));
		}

		// The full content of the oversized entry still comes back uncut.
		var full = await _env.ResultAsync("session.entry", _env.NodeParams("\"index\":4,\"epoch\":0"));
		Assert.Equal(Constants.Remote.Limits.EntryContentMaxChars + 100, full["content"]!.GetValue<string>().Length);
		Assert.Null(full["truncated"]);
	}

	[Fact]
	public async Task Session_entry_errors_by_index_and_epoch_are_unchanged()
	{
		_env.WriteRaw(Mixed());

		var staleEpoch = await _env.RawAsync("session.entry", _env.NodeParams("\"index\":0,\"epoch\":5"));
		var legacyStale = await _env.LegacyRawAsync("session.entry", _env.NodeParams("\"index\":0,\"epoch\":5"));
		var missing = await _env.RawAsync("session.entry", _env.NodeParams("\"index\":99,\"epoch\":0"));
		var legacyMissing = await _env.LegacyRawAsync("session.entry", _env.NodeParams("\"index\":99,\"epoch\":0"));
		var noIndex = await _env.RawAsync("session.entry", _env.NodeParams("\"epoch\":0"));
		var legacyNoIndex = await _env.LegacyRawAsync("session.entry", _env.NodeParams("\"epoch\":0"));

		Assert.Equal(EntryIdsEnv.Canonical(legacyStale), EntryIdsEnv.Canonical(staleEpoch));
		Assert.Equal(EntryIdsEnv.Canonical(legacyMissing), EntryIdsEnv.Canonical(missing));
		Assert.Equal(EntryIdsEnv.Canonical(legacyNoIndex), EntryIdsEnv.Canonical(noIndex));
		Assert.Equal("Session history changed", EntryIdsEnv.ErrorMessage(staleEpoch));
		Assert.Equal("Entry not found", EntryIdsEnv.ErrorMessage(missing));
	}

	[Fact]
	public async Task The_old_beforeIndex_with_afterIndex_error_is_unchanged()
	{
		_env.WriteConversation(3);

		var response = await _env.RawAsync("session.entries", _env.NodeParams("\"beforeIndex\":1,\"afterIndex\":1"));

		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, EntryIdsEnv.ErrorCode(response));
		Assert.Equal("'beforeIndex' and 'afterIndex' cannot be combined", EntryIdsEnv.ErrorMessage(response));
	}

	[Fact]
	public async Task Session_get_equals_the_legacy_answer_apart_from_the_new_fields()
	{
		_env.WriteRaw(Mixed());

		var current = await _env.ResultAsync("session.get", _env.NodeParams());
		var legacy  = await _env.LegacyResultAsync("session.get", _env.NodeParams());

		Assert.Equal(EntryIdsEnv.Canonical(legacy), EntryIdsEnv.Canonical(EntryIdsEnv.Without(current, NewFields)));
	}

	/// <summary>The header-based last-used settings must equal the full-parse ones for every shape of transcript.</summary>
	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	[InlineData(5)]
	public async Task LastUsed_from_the_header_equals_the_full_parse_for_these_transcripts(int shape)
	{
		var text = shape switch
		{
			0 => string.Empty,
			1 => EntryIdsEnv.EntryText("USER", 0, "only a user", "profile=\"work\""),
			2 => EntryIdsEnv.EntryText("ASSISTANT", 0, "only an assistant", "model=\"opus\" effort=\"max\""),
			3 => Mixed(),
			4 => EntryIdsEnv.EntryText("USER", 0, "u", "profile=\"work\"") + EntryIdsEnv.EntryText("ASSISTANT", 1, "a", "model=\"opus\" effort=\"high\"")
				+ EntryIdsEnv.EntryText("ASSISTANT", 2, "a2", "model=\"sonnet\"") + EntryIdsEnv.EntryText("USER", 3, "u2"),
			_ => EntryIdsEnv.EntryText("SYSTEM", 0, "s", "profile=\"work\" model=\"opus\" effort=\"high\"")
				+ EntryIdsEnv.EntryText("USER", 1, "u", "model=\"opus\" effort=\"low\""),
		};
		_env.Models.Models = [new ClaudeModelInfo("opus", "opus", "Opus"), new ClaudeModelInfo("sonnet", "sonnet", "Sonnet")];
		_env.WriteRaw(text);

		var current = await _env.ResultAsync("session.get", _env.NodeParams());
		var legacy  = await _env.LegacyResultAsync("session.get", _env.NodeParams());

		Assert.Equal(EntryIdsEnv.Canonical(legacy["lastUsed"]!), EntryIdsEnv.Canonical(current["lastUsed"]!));
		Assert.Equal(legacy["entryCount"]!.GetValue<int>(), current["entryCount"]!.GetValue<int>());
	}

	[Fact]
	public async Task The_remote_methods_the_web_calls_are_still_registered_remote_callable_and_headers_is_not()
	{
		var relayHeaders = await _env.RawAsync("session.headers", "{\"nodeIds\":[]}", TdcpTransport.Relay);
		Assert.Equal(RemoteProtocol.ErrorCodes.MethodNotFound, EntryIdsEnv.ErrorCode(relayHeaders));

		_env.WriteConversation(1);
		foreach (var method in new[] { "session.get", "session.entries" })
		{
			var response = await _env.RawAsync(method, _env.NodeParams(), TdcpTransport.Relay);
			Assert.Null(response["error"]);
		}
	}

	// ── session.get no longer parses the file ────────────────────────────────────────────────

	[Fact]
	public async Task Session_get_reads_its_header_from_the_store_and_never_parses_the_whole_file()
	{
		_env.WriteRaw(Mixed());
		await _env.ResultAsync("session.get", _env.NodeParams()); // builds the index once

		var before = _env.FileCalls.CountOf(nameof(ISessionFileService.ReadEntries));
		for (var i = 0; i < 5; i++)
			await _env.ResultAsync("session.get", _env.NodeParams());

		Assert.Equal(before, _env.FileCalls.CountOf(nameof(ISessionFileService.ReadEntries)));
		Assert.Equal(0, _env.FileCalls.CountOf(nameof(ISessionFileService.ReadEntries)));
	}

	[Fact]
	public async Task Session_get_stays_correct_after_appends_without_parsing_the_file()
	{
		_env.WriteConversation(4);
		await _env.ResultAsync("session.get", _env.NodeParams());

		_env.Files.AppendMessage(_env.FileName, Constants.SessionFile.RoleAssistant, "new", modelId: "sonnet", effort: "low");
		var get = await _env.ResultAsync("session.get", _env.NodeParams());

		Assert.Equal(5, get["entryCount"]!.GetValue<int>());
		Assert.Equal(5, get["seq"]!.GetValue<long>());
		Assert.Equal(0, _env.FileCalls.CountOf(nameof(ISessionFileService.ReadEntries)));
	}

	// ── session.headers ──────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task Session_headers_returns_one_header_per_session_matching_session_get()
	{
		_env.WriteRaw(Mixed());
		var second = _env.Registry.AddSession(_env.Directory, "Second");
		System.IO.File.WriteAllText(_env.Files.GetFullPath(second.FileName), EntryIdsEnv.ConversationEntry(0), new UTF8Encoding(true));

		var result = await _env.ResultAsync(
			"session.headers",
			$"{{\"nodeIds\":[\"{_env.Session.NodeId}\",\"{second.NodeId}\"]}}",
			TdcpTransport.Loopback);

		var headers = result["headers"]!.AsArray();
		Assert.Equal([_env.Session.NodeId, second.NodeId], headers.Select(h => h!["nodeId"]!.GetValue<string>()));
		Assert.Null(result["missing"]);

		var get = await _env.ResultAsync("session.get", _env.NodeParams());
		var first = headers[0]!;
		Assert.Equal(get["fileId"]!.GetValue<string>(), first["fileId"]!.GetValue<string>());
		Assert.Equal(get["entryCount"]!.GetValue<int>(), first["entryCount"]!.GetValue<int>());
		Assert.Equal(get["seq"]!.GetValue<long>(), first["seq"]!.GetValue<long>());
		Assert.Equal(get["epoch"]!.GetValue<int>(), first["epoch"]!.GetValue<int>());
		Assert.Equal(EntryIdsEnv.Canonical(get["lastUsed"]!), EntryIdsEnv.Canonical(first["lastUsed"]!));
		Assert.Equal(EntryIdsEnv.Start.ToString("O")[..19], first["firstTimestamp"]!.GetValue<string>()[..19]);
		Assert.Equal(EntryIdsEnv.Start.AddMinutes(6).ToString("O")[..19], first["lastTimestamp"]!.GetValue<string>()[..19]);
		Assert.True(first["fileLength"]!.GetValue<long>() > 0);
		Assert.True(first["contentVersion"]!.GetValue<long>() >= 1);
		Assert.Equal(1, headers[1]!["entryCount"]!.GetValue<int>());
	}

	[Fact]
	public async Task Session_headers_lists_unknown_ids_as_missing_and_removes_duplicates()
	{
		_env.WriteConversation(2);

		var result = await _env.ResultAsync(
			"session.headers",
			$"{{\"nodeIds\":[\"{_env.Session.NodeId}\",\"nope\",\"{_env.Session.NodeId}\",\"nope\"]}}",
			TdcpTransport.Loopback);

		Assert.Single(result["headers"]!.AsArray());
		Assert.Equal(["nope"], result["missing"]!.AsArray().Select(m => m!.GetValue<string>()));
	}

	[Fact]
	public async Task Session_headers_of_an_empty_or_missing_transcript_has_no_fileId()
	{
		var result = await _env.ResultAsync("session.headers", $"{{\"nodeIds\":[\"{_env.Session.NodeId}\"]}}", TdcpTransport.Loopback);

		var header = result["headers"]![0]!;
		Assert.Equal(0, header["entryCount"]!.GetValue<int>());
		Assert.Equal(0, header["seq"]!.GetValue<long>());
		Assert.Null(header["firstTimestamp"]);
		Assert.Null(header["lastTimestamp"]);
	}

	[Theory]
	[InlineData("{}")]
	[InlineData("{\"nodeIds\":\"x\"}")]
	[InlineData("{\"nodeIds\":[1]}")]
	[InlineData("{\"nodeIds\":[\"\"]}")]
	[InlineData("{\"nodeIds\":[null]}")]
	public async Task Session_headers_rejects_malformed_params(string paramsJson)
	{
		var response = await _env.RawAsync("session.headers", paramsJson, TdcpTransport.Loopback);

		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, EntryIdsEnv.ErrorCode(response));
	}

	[Fact]
	public async Task Session_headers_accepts_the_maximum_and_rejects_one_more()
	{
		var max = Constants.Remote.Limits.HeadersMaxNodeIds;
		string Ids(int count) => string.Join(",", Enumerable.Range(0, count).Select(i => $"\"n{i}\""));

		var ok = await _env.RawAsync("session.headers", $"{{\"nodeIds\":[{Ids(max)}]}}", TdcpTransport.Loopback);
		Assert.Null(ok["error"]);
		Assert.Equal(max, ok["result"]!["missing"]!.AsArray().Count);

		var tooMany = await _env.RawAsync("session.headers", $"{{\"nodeIds\":[{Ids(max + 1)}]}}", TdcpTransport.Loopback);
		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, EntryIdsEnv.ErrorCode(tooMany));
	}

	[Fact]
	public async Task Session_headers_does_not_parse_any_transcript()
	{
		_env.WriteRaw(Mixed());
		var second = _env.Registry.AddSession(_env.Directory, "Second");
		System.IO.File.WriteAllText(_env.Files.GetFullPath(second.FileName), Mixed(), new UTF8Encoding(true));

		for (var i = 0; i < 3; i++)
			await _env.ResultAsync("session.headers", $"{{\"nodeIds\":[\"{_env.Session.NodeId}\",\"{second.NodeId}\"]}}", TdcpTransport.Loopback);

		Assert.Equal(0, _env.FileCalls.CountOf(nameof(ISessionFileService.ReadEntries)));
	}
}
