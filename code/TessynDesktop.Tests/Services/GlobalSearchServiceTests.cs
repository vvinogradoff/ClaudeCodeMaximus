using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Contract;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>A session index whose rescan only ends when the test says so, and that counts the rescans started.</summary>
/// <remarks>Created by Claude</remarks>
internal class BlockingReindexProxy : DispatchProxy
{
	private int _reindexCalls;

	public TaskCompletionSource<int> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public int ReindexCalls => Volatile.Read(ref _reindexCalls);

	public static IClaudeSessionIndexService Create(out BlockingReindexProxy proxy)
	{
		var created = Create<IClaudeSessionIndexService, BlockingReindexProxy>();
		proxy = (BlockingReindexProxy)(object)created;
		return created;
	}

	protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
	{
		if (targetMethod!.Name == nameof(IClaudeSessionIndexService.ReindexAsync))
		{
			Interlocked.Increment(ref _reindexCalls);
			return Release.Task;
		}

		throw new NotSupportedException(targetMethod.Name);
	}
}

/// <summary>Package J4: <see cref="GlobalSearchService"/> behind <c>search.global</c> and <c>search.reindex</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class GlobalSearchServiceTests : IDisposable
{
	private readonly ImportTestEnv _env = new();
	private readonly FakeSessionIndexActivity _activity = new();
	private readonly GlobalSearchService _sut;

	public GlobalSearchServiceTests()
	{
		_sut = new GlobalSearchService(_env.Index, new RemoteTreeReader(_env.Settings), _activity);
	}

	public void Dispose() => _env.Dispose();

	private static SessionSearchResultModel Hit(string id, double rank, string content = "text", string slug = "p", string? title = null) =>
		new() { ExternalId = id, ProjectSlug = slug, Content = content, Role = "user", Rank = rank, SessionTitle = title };

	[Fact]
	public async Task OneHitPerSession_TheBestRankedMessage_WithAPositiveScore()
	{
		_env.Index.SearchResult = [Hit("a", -9, "best of a"), Hit("b", -8), Hit("a", -7, "second of a")];

		var result = await _sut.SearchAsync("needle", null, null, null);

		Assert.Equal(["a", "b"], result.Hits.Select(h => h.ExternalId));
		Assert.Equal("best of a", result.Hits[0].Snippet);
		Assert.Equal(9, result.Hits[0].Score);
		Assert.Equal(2, result.Total);
	}

	[Fact]
	public async Task TheSearch_LooksAtABoundedNumberOfMessages_InTheGivenProject()
	{
		await _sut.SearchAsync("  needle  ", "C--proj", null, null);

		Assert.Equal("needle", _env.Index.LastSearchQuery);
		Assert.Equal("C--proj", _env.Index.LastSearchProjectSlug);
		Assert.Equal(Constants.GlobalSearch.FetchMessages, _env.Index.LastSearchLimit);
		Assert.Equal(0, _env.Index.LastSearchOffset);
	}

	[Fact]
	public async Task Paging_SlicesTheSessions_AndTheTotalIsTheWholeCount()
	{
		_env.Index.SearchResult = [.. Enumerable.Range(0, 7).Select(i => Hit("s" + i, -10 + i))];

		var page = await _sut.SearchAsync("q", null, 3, 2);

		Assert.Equal(["s2", "s3", "s4"], page.Hits.Select(h => h.ExternalId));
		Assert.Equal(7, page.Total);
	}

	[Fact]
	public async Task TheLimit_IsClampedToTheMaximum_AndTheSnippetIsCut()
	{
		_env.Index.SearchResult = [.. Enumerable.Range(0, 150).Select(i => Hit("s" + i, -i, new string('x', 1000)))];

		var result = await _sut.SearchAsync("q", null, 100_000, null);

		Assert.Equal(Constants.GlobalSearch.MaxLimit, result.Hits.Count);
		Assert.All(result.Hits, h => Assert.Equal(Constants.GlobalSearch.SnippetMaxChars, h.Snippet.Length));
	}

	[Fact]
	public async Task AHitOfASessionTheTreeImports_NamesItsNode_ForBothIdKinds()
	{
		var directory = _env.AddDirectory("proj");
		var byExternalId = _env.Registry.AddSession(directory, "A");
		byExternalId.ExternalId = "ext-1";
		var byClaudeId = _env.Registry.AddSession(directory, "B");
		byClaudeId.ClaudeSessionId = "claude-2";
		_env.Index.SearchResult = [Hit("ext-1", -3, title: "T1"), Hit("claude-2", -2), Hit("unknown", -1)];

		var result = await _sut.SearchAsync("q", null, null, null);

		Assert.Equal([byExternalId.NodeId, byClaudeId.NodeId, null], result.Hits.Select(h => h.ImportedNodeId));
		Assert.Equal("T1", result.Hits[0].Title);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task ABlankQuery_IsRefused(string query) =>
		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => _sut.SearchAsync(query, null, null, null));

	[Theory]
	[InlineData("a/b")]
	[InlineData("a\\b")]
	[InlineData("C:x")]
	[InlineData("")]
	public async Task ASlugThatLooksLikeAPath_IsRefused(string slug) =>
		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => _sut.SearchAsync("q", slug, null, null));

	[Fact]
	public async Task ATooLongQuery_IsRefused() =>
		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => _sut.SearchAsync(new string('q', 501), null, null, null));

	// ── reindex ──────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task Reindex_StartsOneScanInTheBackground_AndRefusesAnotherWhileItRuns()
	{
		var index = BlockingReindexProxy.Create(out var proxy);
		var sut = new GlobalSearchService(index, new RemoteTreeReader(_env.Settings), _activity);

		var first = sut.StartReindex();
		SpinWait.SpinUntil(() => proxy.ReindexCalls == 1, TimeSpan.FromSeconds(5));
		var second = sut.StartReindex();

		Assert.True(first.Started);
		Assert.False(second.Started);
		Assert.Equal(1, proxy.ReindexCalls);

		proxy.Release.SetResult(3);
		Assert.True(SpinWait.SpinUntil(() => sut.StartReindex().Started, TimeSpan.FromSeconds(5)));   // free again once the scan ended
		await Task.CompletedTask;
	}

	[Fact]
	public void Reindex_IsRefused_WhileAnotherScanOfTheIndexRuns()
	{
		var index = BlockingReindexProxy.Create(out var proxy);
		var sut = new GlobalSearchService(index, new RemoteTreeReader(_env.Settings), _activity);
		_activity.ActiveScans = 1;   // the startup scan

		Assert.False(sut.StartReindex().Started);
		Assert.Equal(0, proxy.ReindexCalls);
	}
}
