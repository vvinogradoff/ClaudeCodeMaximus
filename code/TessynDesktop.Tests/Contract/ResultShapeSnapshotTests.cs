using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;
using Xunit;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// Golden JSON snapshots of remote-method result shapes on a seeded temp registry (P1.D), run through both the in-process
/// dispatcher and a real loopback <see cref="TdcpServer"/>. A snapshot changes only when the wire contract changes; set the
/// environment variable <c>TESSYN_UPDATE_GOLDEN=1</c> to rewrite the files, then review the diff.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ResultShapeSnapshotTests : IDisposable
{
	/// <summary>The 21 remote methods the web contract (docs/refactoring/tdcp-spec.md) is frozen on.</summary>
	private static readonly string[] ExpectedRemoteMethods =
	[
		"client.hello",
		"tree.snapshot", "tree.workers", "tree.search", "tree.createSession",
		"recent.list", "recent.hide",
		"tags.search",
		"session.get", "session.entries", "session.entry", "session.send", "session.stop", "session.setFavorite", "session.setStreaming", "session.compact",
		"attention.list", "attention.reply", "attention.dismiss",
		"catalog.get",
		"usage.get",
	];

	private readonly RemoteTreeTestEnv _env = new();
	private readonly TdcpTestHost _host;
	private readonly string _supervisorId;

	public ResultShapeSnapshotTests()
	{
		_host = new TdcpTestHost(_env.Dispatcher);
		var dir = _env.AddDirectory("proj");
		var group = _env.Registry.AddGroup(dir, "Group");
		var sup = _env.AddSession(dir, "Supervisor");
		_supervisorId = sup.NodeId;
		_env.AddSession(dir, "Worker", supervisorNodeId: sup.NodeId);
		var inGroup = _env.AddSession(dir, "InGroup", group);
		inGroup.IsFavorite = true;
		_env.Activity.Set(sup.NodeId, new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero));
		_env.Activity.Set(inGroup.NodeId, new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero), hasDraft: true);
	}

	public void Dispose()
	{
		_host.Dispose();
		_env.Dispose();
	}

	public static TheoryData<string, string> Snapshots => new()
	{
		{ "tree.snapshot", "{}" },
		{ "tree.workers", "{\"supervisorId\":\"$SUP\"}" },
		{ "recent.list", "{}" },
		{ "tags.search", "{}" },
	};

	[Fact]
	public void The_remote_method_names_are_the_21_of_the_web_contract()
	{
		var names = typeof(Constants.Remote.Methods)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(f => f.IsLiteral && f.FieldType == typeof(string))
			.Select(f => (string)f.GetRawConstantValue()!)
			.OrderBy(n => n, StringComparer.Ordinal)
			.ToArray();

		Assert.Equal(ExpectedRemoteMethods.OrderBy(n => n, StringComparer.Ordinal), names);
		Assert.Equal(21, names.Length);
	}

	[Theory]
	[MemberData(nameof(Snapshots))]
	public async Task In_process_result_matches_the_golden_file(string method, string paramsJson)
	{
		var result = await CallInProcessAsync(method, Seeded(paramsJson));

		AssertGolden(method, result);
	}

	[Theory]
	[MemberData(nameof(Snapshots))]
	public async Task Loopback_http_result_matches_the_same_golden_file(string method, string paramsJson)
	{
		var response = await _host.CallAsync(method, JsonNode.Parse(Seeded(paramsJson)));

		Assert.Null(response["error"]);
		AssertGolden(method, response["result"]!);
	}

	// â”€â”€ helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

	private string Seeded(string paramsJson) =>
		paramsJson.Replace("$SUP", _supervisorId);

	private async Task<JsonNode> CallInProcessAsync(string method, string paramsJson)
	{
		var request = $$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{paramsJson}}}""";
		var response = await _env.Dispatcher.HandleTextAsync(request, TdcpTransport.Relay, CancellationToken.None);
		Assert.Null(response!["error"]);
		return response["result"]!;
	}

	private void AssertGolden(string method, JsonNode result)
	{
		var actual = Eol(Normalize(result.ToJsonString()));
		var path = GoldenPath(method);

		if (Environment.GetEnvironmentVariable("TESSYN_UPDATE_GOLDEN") == "1")
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, actual + "\n");
		}

		Assert.True(File.Exists(path), $"Missing golden file {path}; run once with TESSYN_UPDATE_GOLDEN=1");
		Assert.Equal(Eol(File.ReadAllText(path)).TrimEnd(), actual.TrimEnd());
	}

	/// <summary>Line endings differ between the indented JSON writer (Environment.NewLine) and a checkout (LF or CRLF).</summary>
	private static string Eol(string text) => text.Replace("\r\n", "\n");

	/// <summary>Replaces GUIDs (numbered by first appearance), temp paths and timestamps so the snapshot is stable across runs.</summary>
	private string Normalize(string json)
	{
		var ids = new System.Collections.Generic.Dictionary<string, string>();
		json = Regex.Replace(json, "[0-9a-f]{32}", m =>
		{
			if (!ids.TryGetValue(m.Value, out var placeholder))
				ids[m.Value] = placeholder = "<id" + (ids.Count + 1) + ">";
			return placeholder;
		});
		json = json.Replace(JsonEncodedRoot(), "<root>");
		// Path separators after the root are OS-specific (JSON-escaped backslash on Windows): make them '/'.
		json = Regex.Replace(json, "<root>[^\"]*", m => m.Value.Replace("\\\\", "/"));
		json = Regex.Replace(json, "\"serverTimeUtc\":\"[^\"]*\"", "\"serverTimeUtc\":\"<ts>\"");
		var node = JsonNode.Parse(json)!;
		return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
	}

	private string JsonEncodedRoot() => JsonSerializer.Serialize(_env.Root)[1..^1];

	private static string GoldenPath(string method, [CallerFilePath] string thisFile = "") =>
		Path.Combine(Path.GetDirectoryName(thisFile)!, "Golden", method + ".json");
}


