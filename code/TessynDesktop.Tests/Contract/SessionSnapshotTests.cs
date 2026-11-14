using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;
using Xunit;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// Golden JSON snapshots of the session methods (<c>session.get</c>, <c>session.entries</c>, <c>session.entry</c>,
/// <c>session.headers</c>) and of the P1.F events on a seeded real session file (the P1.D suite had none for <c>session.*</c>).
/// Run through the in-process dispatcher and a real loopback <see cref="TdcpServer"/>. Set <c>TESSYN_UPDATE_GOLDEN=1</c> to
/// rewrite the files, then review the diff.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionSnapshotTests : IDisposable
{
	private readonly EntryIdsEnv _env = new();
	private readonly TdcpTestHost _host;

	public SessionSnapshotTests()
	{
		_env.WriteRaw(
			EntryIdsEnv.EntryText("USER", 0, "Hello there", "profile=\"work\"")
			+ EntryIdsEnv.EntryText("ASSISTANT", 1, "Hi! Second sentence", "model=\"opus\" effort=\"high\"")
			+ EntryIdsEnv.EntryText("USER", 2, "A longer question that the client may want to read in slices")
			+ EntryIdsEnv.EntryText("ASSISTANT", 3, "[x] checklist and [link](http://example.test)"));
		_host = new TdcpTestHost(_env.Dispatcher);
	}

	public void Dispose()
	{
		_host.Dispose();
		_env.Dispose();
	}

	/// <summary>(golden file, method, params with <c>$NODE</c> for the session and <c>$FILE</c> for its current fileId).</summary>
	public static TheoryData<string, string, string> Snapshots => new()
	{
		{ "session.get", "session.get", "{\"nodeId\":\"$NODE\"}" },
		{ "session.entries", "session.entries", "{\"nodeId\":\"$NODE\",\"limit\":3}" },
		{ "session.entries.afterSeq", "session.entries", "{\"nodeId\":\"$NODE\",\"afterSeq\":2,\"limit\":5}" },
		{ "session.entries.beforeEntryId", "session.entries", "{\"nodeId\":\"$NODE\",\"beforeEntryId\":\"$FILE:2\",\"limit\":5}" },
		{ "session.entry", "session.entry", "{\"nodeId\":\"$NODE\",\"entryId\":\"$FILE:1\"}" },
		{ "session.entry.range", "session.entry", "{\"nodeId\":\"$NODE\",\"entryId\":\"$FILE:2\",\"range\":{\"offset\":9,\"length\":14}}" },
		{ "session.entry.legacy", "session.entry", "{\"nodeId\":\"$NODE\",\"index\":1,\"epoch\":0}" },
		{ "session.headers", "session.headers", "{\"nodeIds\":[\"$NODE\",\"unknown-node\"]}" },
	};

	[Theory]
	[MemberData(nameof(Snapshots))]
	public async Task In_process_result_matches_the_golden_file(string golden, string method, string paramsJson)
	{
		var result = await _env.ResultAsync(method, Seeded(paramsJson), TdcpTransport.Loopback);

		AssertGolden(golden, result);
	}

	[Theory]
	[MemberData(nameof(Snapshots))]
	public async Task Loopback_http_result_matches_the_same_golden_file(string golden, string method, string paramsJson)
	{
		var response = await _host.CallAsync(method, JsonNode.Parse(Seeded(paramsJson)));

		Assert.Null(response["error"]);
		AssertGolden(golden, response["result"]!);
	}

	// The event payloads are built from the DTOs with fixed values, so the shape is pinned independently of clocks; the
	// publisher tests prove the values.

	[Fact]
	public void The_entries_appended_event_with_inline_entries_matches_the_golden_file()
	{
		var fileId = new string('a', 32);
		var payload = new EntriesAppendedPayload(
			_env.Session.NodeId, fileId, 1, 3,
			[
				new SessionEntryDto { Index = 1, Timestamp = EntryIdsEnv.Start.AddMinutes(1), Role = "assistant", Content = "Hi!", ModelId = "opus", Effort = "high", EntryId = fileId + ":1" },
				new SessionEntryDto { Index = 2, Timestamp = EntryIdsEnv.Start.AddMinutes(2), Role = "user", Content = "Next", EntryId = fileId + ":2" },
			]);

		AssertGolden("entries.appended", JsonSerializer.SerializeToNode(payload, TdcpJsonOptions.Default)!);
	}

	[Fact]
	public void The_entries_appended_event_ids_only_matches_the_golden_file()
	{
		var payload = new EntriesAppendedPayload(_env.Session.NodeId, new string('b', 32), 10, 12);

		var node = JsonSerializer.SerializeToNode(payload, TdcpJsonOptions.Default)!;

		Assert.False(node.AsObject().ContainsKey("entries"));
		AssertGolden("entries.appended.idsOnly", node);
	}

	[Fact]
	public void The_session_reset_event_matches_the_golden_file_and_keeps_the_old_fields()
	{
		var payload = new SessionResetPayload(_env.Session.NodeId, 1, new string('c', 32));

		var node = JsonSerializer.SerializeToNode(payload, TdcpJsonOptions.Default)!;

		Assert.NotNull(node["nodeId"]);
		Assert.NotNull(node["epoch"]);
		AssertGolden("session.reset", node);
	}

	[Fact]
	public void The_session_appended_event_is_unchanged()
	{
		var node = JsonSerializer.SerializeToNode(new SessionEpochPayload(_env.Session.NodeId, 0), TdcpJsonOptions.Default)!;

		AssertGolden("session.appended", node);
	}

	// ── helpers ──────────────────────────────────────────────────────────────────────────────

	private string Seeded(string paramsJson)
	{
		var page   = _env.ResultAsync("session.entries", _env.NodeParams(), TdcpTransport.Loopback).GetAwaiter().GetResult();
		var fileId = page["fileId"]!.GetValue<string>();
		return paramsJson.Replace("$NODE", _env.Session.NodeId).Replace("$FILE", fileId);
	}

	private void AssertGolden(string name, JsonNode result)
	{
		var actual = Eol(Normalize(result.ToJsonString()));
		var path   = GoldenPath(name);

		if (Environment.GetEnvironmentVariable("TESSYN_UPDATE_GOLDEN") == "1")
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, actual + "\n");
		}

		Assert.True(File.Exists(path), $"Missing golden file {path}; run once with TESSYN_UPDATE_GOLDEN=1");
		Assert.Equal(Eol(File.ReadAllText(path)).TrimEnd(), actual.TrimEnd());
	}

	private static string Eol(string text) => text.Replace("\r\n", "\n");

	/// <summary>Replaces 32-digit ids (numbered by first appearance), temp paths and the machine-specific bits so the snapshot is stable.</summary>
	private string Normalize(string json)
	{
		var ids = new System.Collections.Generic.Dictionary<string, string>();
		json = Regex.Replace(json, "[0-9a-f]{32}", m =>
		{
			if (!ids.TryGetValue(m.Value, out var placeholder))
				ids[m.Value] = placeholder = "<id" + (ids.Count + 1) + ">";
			return placeholder;
		});
		json = json.Replace(JsonSerializer.Serialize(_env.Root)[1..^1], "<root>");
		json = Regex.Replace(json, "<root>[^\"]*", m => m.Value.Replace("\\\\", "/"));
		var node = JsonNode.Parse(json)!;
		return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
	}

	private static string GoldenPath(string name, [CallerFilePath] string thisFile = "") =>
		Path.Combine(Path.GetDirectoryName(thisFile)!, "Golden", name + ".json");
}
