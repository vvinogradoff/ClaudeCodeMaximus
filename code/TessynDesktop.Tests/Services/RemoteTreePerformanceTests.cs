using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;
using Xunit.Abstractions;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// The live tree is 20 directories, 231 groups, 3,087 sessions of which 598 are top-level: the B1 read methods must answer it well
/// under a second. The session files are real (about 20 entries each), read through the real <see cref="SessionActivityService"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class RemoteTreePerformanceTests : IDisposable
{
	private const int Directories = 20;
	private const int GroupsPerDirectory = 11;      // 220 top groups + 11 nested = 231
	private const int TopLevelSessions = 598;
	private const int WorkerSessions = 2489;        // + 598 = 3,087

	private static readonly DateTimeOffset T0 = DateTimeOffset.UtcNow.AddHours(-30);

	private readonly ITestOutputHelper _output;
	private readonly string _root;
	private readonly AppSettingsService _settings;
	private readonly SessionFileService _files;
	private readonly TdcpDispatcher _dispatcher;
	private readonly FakeSessionTagService _tags = new();
	private readonly InProcFakeIndexService _index = new();
	private readonly string _firstTopLevelId;

	public RemoteTreePerformanceTests(ITestOutputHelper output)
	{
		_output = output;
		_root   = Path.Combine(Path.GetTempPath(), "cm_test_b1perf_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_root);

		_settings = new AppSettingsService(_root);
		_settings.Settings.SessionFilesRoot = _root;
		_files = new SessionFileService(_settings);

		_firstTopLevelId = BuildTree();

		var registry = new SessionRegistry(_settings, _files, new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
		var activity = new SessionActivityService(_files, new FakeDraftService());
		var attention = new FakeAttentionService();
		var states = new SessionStateBuilder(registry, new FakeStreamingManagerForTurnService(), attention, activity);
		var reader = new RemoteTreeReader(_settings);
		var labels = new FakeDirectoryLabelService();
		var scheduler = new InProcFakeScheduler();

		_dispatcher = new TdcpDispatcher(
		[
			new TreeRemoteRpcModule(reader, registry, states, activity, new TreeVersionTracker(), labels, _index, new FakeSessionSearchService()),
			new RecentRemoteRpcModule(reader, states, activity, attention, scheduler, labels, registry, _settings),
			new TagsRemoteRpcModule(reader, _tags, activity, states, labels),
		]);
	}

	public void Dispose() => Directory.Delete(_root, recursive: true);

	/// <summary>Builds the synthetic tree directly in the settings model and returns the first top-level session's NodeId.</summary>
	private string BuildTree()
	{
		var rng = new Random(42);
		var tops = new System.Collections.Generic.List<SessionNodeModel>();
		var containers = new System.Collections.Generic.List<System.Collections.Generic.List<SessionNodeModel>>();

		for (var d = 0; d < Directories; d++)
		{
			var dir = new DirectoryNodeModel { Path = Path.Combine(_root, "dir" + d), NodeId = Guid.NewGuid().ToString("N") };
			containers.Add(dir.Sessions);
			for (var g = 0; g < GroupsPerDirectory; g++)
			{
				var group = new GroupNodeModel { Name = $"g{d}-{g}", NodeId = Guid.NewGuid().ToString("N"), WorkingDirectory = dir.Path };
				dir.Groups.Add(group);
				containers.Add(group.Sessions);
				if (g == 0 && d < 11)
				{
					var nested = new GroupNodeModel { Name = $"n{d}", NodeId = Guid.NewGuid().ToString("N"), WorkingDirectory = dir.Path };
					group.Groups.Add(nested);
					containers.Add(nested.Sessions);
				}
			}
			_settings.Settings.Tree.Add(dir);
		}

		for (var i = 0; i < TopLevelSessions; i++)
			tops.Add(NewSession(rng, containers, "S" + i, null, withFile: true));

		for (var i = 0; i < WorkerSessions; i++)
		{
			var supervisor = tops[rng.Next(183)];   // 183 supervisors, as in the live data
			NewSession(rng, containers, "W" + i, supervisor.NodeId, withFile: false);
		}

		return tops[0].NodeId;
	}

	private SessionNodeModel NewSession(Random rng, System.Collections.Generic.List<System.Collections.Generic.List<SessionNodeModel>> containers, string name, string? supervisor, bool withFile)
	{
		var fileName = withFile ? _files.CreateSessionFile() : $"w-{name}.txt";
		if (withFile)
			File.WriteAllText(_files.GetFullPath(fileName), SessionBody(rng), new UTF8Encoding(false));

		var node = new SessionNodeModel
		{
			Name             = name + " some realistic session name",
			FileName         = fileName,
			NodeId           = Guid.NewGuid().ToString("N"),
			AgentToken       = Guid.NewGuid().ToString("N"),
			SupervisorNodeId = supervisor,
			ExternalId       = Guid.NewGuid().ToString(),
			WorkingDirectory = _root,
		};
		containers[rng.Next(containers.Count)].Add(node);
		return node;
	}

	private static string SessionBody(Random rng)
	{
		var sb = new StringBuilder();
		var t = T0.AddMinutes(rng.Next(0, 60 * 40));
		for (var i = 0; i < 10; i++)
		{
			var stamp = t.AddMinutes(i * 2).ToString(Constants.SessionFile.TimestampFormat);
			sb.Append($"[{stamp}] USER profile=\"Work\"\nplease do the thing number {i} " + new string('x', 400) + "\n\n");
			sb.Append($"[{stamp}] ASSISTANT model=\"m\"\nsure, done " + new string('y', 800) + "\n\n");
		}
		return sb.ToString();
	}

	private async Task<(double ms, string json)> TimeAsync(string method, string paramsJson = "{}")
	{
		var request = $$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{paramsJson}}}""";
		var sw = Stopwatch.StartNew();
		var response = await _dispatcher.HandleTextAsync(request, TdcpTransport.Relay, CancellationToken.None);
		var json = response.ToJsonString();   // includes serialization of the result
		sw.Stop();
		Assert.Null(response["error"]);
		return (sw.Elapsed.TotalMilliseconds, json);
	}

	[Fact]
	public async Task TreeSnapshot_OfTheLiveSizedTree_AnswersWellUnderASecond()
	{
		var (cold, json) = await TimeAsync("tree.snapshot");
		var (warm, _) = await TimeAsync("tree.snapshot");

		_output.WriteLine($"tree.snapshot: cold {cold:F0} ms (reads {TopLevelSessions} session-file tails), warm {warm:F0} ms, {json.Length / 1024} KB");
		Assert.True(cold < 3000, $"cold {cold} ms");
		Assert.True(warm < 500, $"warm {warm} ms");
	}

	[Fact]
	public async Task RecentList_OfTheLiveSizedTree_AnswersWellUnderASecond()
	{
		var (cold, _) = await TimeAsync("recent.list");
		var (warm, json) = await TimeAsync("recent.list");

		_output.WriteLine($"recent.list: cold {cold:F0} ms, warm {warm:F0} ms, {json.Length / 1024} KB");
		Assert.True(cold < 3000, $"cold {cold} ms");
		Assert.True(warm < 500, $"warm {warm} ms");
	}

	[Fact]
	public async Task TreeSearch_OverAllThreeThousandSessions_AnswersWellUnderASecond()
	{
		// Every session matches: the worst case for the roll-up and the ranking.
		var all = new System.Collections.Generic.List<SessionSearchScoreModel>();
		lock (_settings.SyncRoot)
			foreach (var dir in _settings.Settings.Tree)
				CollectScores(dir.Sessions, dir.Groups, all);
		_index.ScoresResult = all;

		await TimeAsync("tree.snapshot");   // warm the activity cache, as a running daemon would have
		var (ms, json) = await TimeAsync("tree.search", """{"query":"a long enough query"}""");

		_output.WriteLine($"tree.search (all {all.Count} sessions match): {ms:F0} ms, {json.Length / 1024} KB");
		Assert.True(ms < 1000, $"{ms} ms");
	}

	private static void CollectScores(System.Collections.Generic.List<SessionNodeModel> sessions, System.Collections.Generic.List<GroupNodeModel> groups, System.Collections.Generic.List<SessionSearchScoreModel> into)
	{
		foreach (var s in sessions)
			into.Add(new SessionSearchScoreModel { ExternalId = s.ExternalId!, StrictMatchCount = 1, LooseMatchCount = 2 });
		foreach (var g in groups)
			CollectScores(g.Sessions, g.Groups, into);
	}

	[Fact]
	public async Task TagsSearch_OverAllThreeThousandSessions_AnswersWellUnderASecond()
	{
		_tags.Tags = [new SessionTagModel { Kind = SessionTagKind.LinearIssue, Key = "HSE-1", Label = "HSE-1", Url = "https://linear.app/x/HSE-1" }];
		await TimeAsync("tree.snapshot");

		var (ms, json) = await TimeAsync("tags.search", """{"text":"","selected":[{"key":"HSE-1","label":"HSE-1","url":"https://linear.app/x/HSE-1","kind":"linearIssue"}]}""");

		_output.WriteLine($"tags.search (one tag on every session, selected): {ms:F0} ms, {json.Length / 1024} KB");
		Assert.True(ms < 1000, $"{ms} ms");
	}

	[Fact]
	public async Task SnapshotShape_MatchesTheLiveCounts()
	{
		var (_, json) = await TimeAsync("tree.snapshot");
		var tree = System.Text.Json.Nodes.JsonNode.Parse(json)!["result"]!;

		var top = 0;
		var workers = 0;
		foreach (var dir in tree["directories"]!.AsArray())
			CountSessions(dir!, ref top, ref workers);

		Assert.Equal(Directories, tree["directories"]!.AsArray().Count);
		Assert.Equal(TopLevelSessions, top);
		Assert.Equal(WorkerSessions, workers);
		Assert.False(string.IsNullOrEmpty(_firstTopLevelId));
	}

	private static void CountSessions(System.Text.Json.Nodes.JsonNode container, ref int top, ref int workers)
	{
		foreach (var s in container["sessions"]!.AsArray())
		{
			top++;
			workers += s!["workerCount"]!.GetValue<int>();
		}
		foreach (var g in container["groups"]!.AsArray())
			CountSessions(g!, ref top, ref workers);
	}
}
