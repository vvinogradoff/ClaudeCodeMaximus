using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>Tree load, lazy workers, search and version-based refresh against a fake client.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeViewModelTests
{
	private readonly TreeBuilder _b = new();
	private readonly FakeTessynClient _client = new();

	private TreeViewModel CreateTree() =>
		new(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);

	private void SeedTree()
	{
		_client.Directories =
		[
			_b.Directory("d1", "Repo", [_b.Group("g1", "Group A", _b.Session("s2", "In group"))], _b.Session("s1", "Top session", workers: 2)),
			_b.Directory("d2", "Other", [], _b.Session("s3", "Third")),
		];
		_client.Workers["s1"] = [_b.Session("w1", "Worker 1", supervisor: "s1"), _b.Session("w2", "Worker 2", supervisor: "s1")];
	}

	[Fact]
	public async Task Load_ShowsDirectoriesCollapsedAndCountsSessions()
	{
		SeedTree();
		using var tree = CreateTree();

		await tree.LoadAsync();

		Assert.Equal(["d1", "d2"], tree.Rows.Select(r => r.Id));
		Assert.Equal(3, tree.SessionCount);
		Assert.Equal(1, tree.Version);
		Assert.Equal(["tree.snapshot:full"], _client.Calls);
	}

	[Fact]
	public async Task Expand_ShowsGroupsThenSessionsInOrderAndIndentsByDepth()
	{
		SeedTree();
		using var tree = CreateTree();
		await tree.LoadAsync();

		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows[0]);
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows[1]);

		Assert.Equal(["d1", "g1", "s2", "s1", "d2"], tree.Rows.Select(r => r.Id));
		Assert.Equal(0, tree.Rows[0].Depth);
		Assert.Equal(2, tree.Rows[2].Depth);
		Assert.Equal(2 * TreeNodeViewModel.IndentPerLevel, tree.Rows[2].IndentWidth);
	}

	[Fact]
	public async Task Workers_AreLoadedLazilyOnFirstExpandOnly()
	{
		SeedTree();
		using var tree = CreateTree();
		await tree.LoadAsync();
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows[0]);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("tree.workers", StringComparison.Ordinal));
		var session = tree.Rows.Single(r => r.Id == "s1");
		Assert.True(session.CanExpand);

		await tree.ToggleExpandCommand.ExecuteAsync(session);
		await tree.ToggleExpandCommand.ExecuteAsync(session);
		await tree.ToggleExpandCommand.ExecuteAsync(session);

		Assert.Equal(1, _client.Calls.Count(c => c == "tree.workers:s1"));
		Assert.Contains(tree.Rows, r => r.Id == "w1" && r.Kind == TreeNodeKind.Worker && r.Depth == 2);
	}

	[Fact]
	public async Task TreeChanged_WithNewVersion_RefetchesWithSinceVersion()
	{
		SeedTree();
		using var tree = CreateTree();
		await tree.LoadAsync();
		_client.Directories = [_b.Directory("d9", "New", [], _b.Session("n1"))];
		_client.TreeVersion = 2;

		_client.RaiseNotification("tree.changed", new TreeChangedPayload(2));
		await tree.WhenIdleAsync();

		Assert.Equal("tree.snapshot:1", _client.Calls.Last(c => c.StartsWith("tree.snapshot", StringComparison.Ordinal)));
		Assert.Equal(["d9"], tree.Rows.Select(r => r.Id));
		Assert.Equal(2, tree.Version);
	}

	[Fact]
	public async Task TreeChanged_WithKnownVersion_DoesNotCallTheDaemon()
	{
		SeedTree();
		using var tree = CreateTree();
		await tree.LoadAsync();
		var calls = _client.Calls.Count;

		_client.RaiseNotification("tree.changed", new TreeChangedPayload(1));
		await tree.WhenIdleAsync();

		Assert.Equal(calls, _client.Calls.Count);
	}

	[Fact]
	public async Task Refresh_KeepsExpandedNodesAndReloadsTheirWorkers()
	{
		SeedTree();
		using var tree = CreateTree();
		await tree.LoadAsync();
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows[0]);
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows.Single(r => r.Id == "s1"));
		_client.Workers["s1"] = [_b.Session("w1", "Worker 1", supervisor: "s1"), _b.Session("w2", "Worker 2", supervisor: "s1"), _b.Session("w3", "Worker 3", supervisor: "s1")];
		_client.TreeVersion = 2;

		_client.RaiseNotification("tree.changed", new TreeChangedPayload(2));
		await tree.WhenIdleAsync();

		Assert.Equal(["d1", "g1", "s1", "w1", "w2", "w3", "d2"], tree.Rows.Select(r => r.Id));
		Assert.Equal(2, _client.Calls.Count(c => c == "tree.workers:s1"));
	}

	[Fact]
	public async Task Resync_ForcesAFullSnapshot()
	{
		SeedTree();
		using var tree = CreateTree();
		await tree.LoadAsync();

		_client.RaiseResync();
		await tree.WhenIdleAsync();

		Assert.Equal(2, _client.Calls.Count(c => c == "tree.snapshot:full"));
	}

	[Fact]
	public async Task SessionChanged_UpdatesTheRowState()
	{
		SeedTree();
		using var tree = CreateTree();
		await tree.LoadAsync();
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows[1]);
		var row = tree.Rows.Single(r => r.Id == "s3");
		Assert.False(row.IsRunning);

		_client.RaiseNotification("session.changed", new SessionChangedPayload(new SessionStateDto("s3", true, true, new RowAttentionDto(2, AttentionKind.Ask), true, null, null, false, null)));

		Assert.True(row.IsRunning);
		Assert.Equal(2, row.AttentionCount);
		Assert.True(row.IsFavorite);
	}

	[Fact]
	public async Task Search_ShowsOnlyMatchingTopLevelSessionsAndRestoresTheTree()
	{
		SeedTree();
		_client.SearchHandler = q => new TreeSearchResult(q, false, false, false, [new TreeSearchMatchDto("s1", 2, 3, true, ["w2"])]);
		using var tree = CreateTree();
		await tree.LoadAsync();

		tree.SearchText = "needle";
		await tree.WhenIdleAsync();

		Assert.Equal(["s1"], tree.Rows.Select(r => r.Id));
		Assert.True(tree.IsSearching);
		Assert.Contains("tree.search:needle", _client.Calls);

		tree.SearchText = "";
		await tree.WhenIdleAsync();

		Assert.False(tree.IsSearching);
		Assert.Equal(["d1", "d2"], tree.Rows.Select(r => r.Id));
	}

	[Fact]
	public async Task Search_ExpandingAMatchShowsOnlyTheMatchingWorkers()
	{
		SeedTree();
		_client.SearchHandler = q => new TreeSearchResult(q, false, false, false, [new TreeSearchMatchDto("s1", 1, 1, false, ["w2"])]);
		using var tree = CreateTree();
		await tree.LoadAsync();
		tree.SearchText = "x";
		await tree.WhenIdleAsync();

		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows[0]);

		Assert.Equal(["s1", "w2"], tree.Rows.Select(r => r.Id));
	}

	[Fact]
	public async Task Search_IsDebouncedSoOnlyTheLastQueryReachesTheDaemon()
	{
		SeedTree();
		using var tree = new TreeViewModel(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.FromMilliseconds(150));
		await tree.LoadAsync();

		tree.SearchText = "a";
		tree.SearchText = "ab";
		tree.SearchText = "abc";
		await tree.WhenIdleAsync();

		Assert.Equal(["tree.search:abc"], _client.Calls.Where(c => c.StartsWith("tree.search", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task Selection_RaisesTheEventAndSurvivesARefresh()
	{
		SeedTree();
		using var tree = CreateTree();
		await tree.LoadAsync();
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows[1]);
		var selected = new List<string>();
		tree.SelectionChanged += (_, node) => selected.Add(node.Id);

		tree.SelectedNode = tree.Rows.Single(r => r.Id == "s3");
		_client.TreeVersion = 2;
		_client.RaiseNotification("tree.changed", new TreeChangedPayload(2));
		await tree.WhenIdleAsync();

		Assert.Equal(["s3"], selected);
		Assert.Equal("s3", tree.SelectedNode?.Id);
		Assert.Equal("s3", tree.SelectedNodeId);
		Assert.DoesNotContain(_client.Calls, c => c.Contains("setSelected", StringComparison.Ordinal));
	}

	[Fact]
	public async Task RestoreExpansion_AndSelectNodeById_ExpandAncestorsAndSelect()
	{
		SeedTree();
		using var tree = CreateTree();
		tree.RestoreExpansion(["d1"]);
		await tree.LoadAsync();
		Assert.Equal(["d1", "g1", "s1", "d2"], tree.Rows.Select(r => r.Id));

		var raised = new List<string>();
		tree.SelectionChanged += (_, node) => raised.Add(node.Id);

		Assert.True(tree.SelectNodeById("s2"));
		Assert.False(tree.SelectNodeById("unknown"));

		Assert.Equal(["d1", "g1", "s2", "s1", "d2"], tree.Rows.Select(r => r.Id));
		Assert.Equal(["s2"], raised);
		Assert.Contains("g1", tree.ExpandedNodeIds);
	}

	[Fact]
	public async Task FirstLoad_WithoutClientState_UsesTheDaemonExpansionFlagsAsInitialState()
	{
		_client.Directories = [new TreeDirectoryDto("d1", "Repo", true, [], [_b.Session("s1")])];
		using var tree = CreateTree();

		await tree.LoadAsync();

		Assert.Equal(["d1", "s1"], tree.Rows.Select(r => r.Id));
		Assert.Contains("d1", tree.ExpandedNodeIds);
	}
}
