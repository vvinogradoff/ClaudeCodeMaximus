using System;
using System.IO;
using System.Linq;
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
/// Golden shapes of <c>SessionEntryDto.Source</c> (additive field): on <c>session.entries</c> over a real session file served through the
/// content store, with the legacy "[Scheduled]" prefix, and on the <c>entries.appended</c> event. Set <c>TESSYN_UPDATE_GOLDEN=1</c> to
/// rewrite the files, then review the diff.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class EntrySourceSnapshotTests : IDisposable
{
	private readonly EntryIdsEnv _env = new();

	public EntrySourceSnapshotTests()
	{
		_env.WriteRaw(
			EntryIdsEnv.EntryText("USER", 0, "typed on the desktop", "profile=\"work\"")
			+ EntryIdsEnv.EntryText("ASSISTANT", 1, "ok", "model=\"opus\" effort=\"high\"")
			+ EntryIdsEnv.EntryText("USER", 2, "[Scheduled] Nightly check: run it", "profile=\"work\" source=\"scheduled\"")
			+ EntryIdsEnv.EntryText("USER", 3, "from the supervisor", "profile=\"work\" source=\"orchestrated\"")
			+ EntryIdsEnv.EntryText("USER", 4, "typed in Telegram", "source=\"telegram\"")
			+ EntryIdsEnv.EntryText("USER", 5, "typed in the web client", "source=\"web\"")
			+ EntryIdsEnv.EntryText("USER", 6, "[Scheduled] Written before sources existed"));
	}

	public void Dispose() => _env.Dispose();

	[Fact]
	public async Task The_entries_page_carries_each_source_and_matches_the_golden_file()
	{
		var result = await _env.ResultAsync("session.entries", _env.NodeParams());

		var sources = result["entries"]!.AsArray().Select(e => e!["source"]?.GetValue<string>()).ToArray();
		Assert.Equal([null, null, "scheduled", "orchestrated", "telegram", "web", "scheduled"], sources);
		AssertGolden("session.entries.sources", result);
	}

	[Fact]
	public async Task The_same_sources_come_back_without_the_content_store()
	{
		var result = await _env.LegacyResultAsync("session.entries", _env.NodeParams());

		var sources = result["entries"]!.AsArray().Select(e => e!["source"]?.GetValue<string>()).ToArray();
		Assert.Equal([null, null, "scheduled", "orchestrated", "telegram", "web", "scheduled"], sources);
	}

	[Fact]
	public async Task A_single_entry_read_carries_the_source()
	{
		var result = await _env.ResultAsync("session.entry", _env.NodeParams("\"index\":3,\"epoch\":0"));

		Assert.Equal("orchestrated", result["source"]!.GetValue<string>());
	}

	[Fact]
	public void The_entries_appended_event_with_a_source_matches_the_golden_file()
	{
		var fileId = new string('a', 32);
		var payload = new EntriesAppendedPayload(
			_env.Session.NodeId, fileId, 1, 3,
			[
				new SessionEntryDto { Index = 1, Timestamp = EntryIdsEnv.Start.AddMinutes(1), Role = "user", Content = "tick", Source = "scheduled", EntryId = fileId + ":1" },
				new SessionEntryDto { Index = 2, Timestamp = EntryIdsEnv.Start.AddMinutes(2), Role = "user", Content = "Next", EntryId = fileId + ":2" },
			]);

		AssertGolden("entries.appended.sources", JsonSerializer.SerializeToNode(payload, TdcpJsonOptions.Default)!);
	}

	private void AssertGolden(string name, JsonNode result)
	{
		var actual = Eol(Normalize(result.ToJsonString()));
		var path = GoldenPath(name);

		if (Environment.GetEnvironmentVariable("TESSYN_UPDATE_GOLDEN") == "1")
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, actual + "\n");
		}

		Assert.True(File.Exists(path), $"Missing golden file {path}; run once with TESSYN_UPDATE_GOLDEN=1");
		Assert.Equal(Eol(File.ReadAllText(path)).TrimEnd(), actual.TrimEnd());
	}

	private static string Eol(string text) => text.Replace("\r\n", "\n");

	/// <summary>Replaces 32-digit ids (numbered by first appearance) and the temp root so the snapshot is stable.</summary>
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
		var node = JsonNode.Parse(json)!;
		return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
	}

	private static string GoldenPath(string name, [CallerFilePath] string thisFile = "") =>
		Path.Combine(Path.GetDirectoryName(thisFile)!, "Golden", name + ".json");
}
