using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>The Recent dropdown rules (FR.9.6–FR.9.8): <c>recent.list</c> and <c>recent.hide</c> (contract 3.6, 3.7).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class RecentRemoteRpcModuleTests : IDisposable
{
	private readonly RemoteTreeTestEnv _env = new();
	private readonly DirectoryNodeModel _dir;

	public RecentRemoteRpcModuleTests() => _dir = _env.AddDirectory("proj");

	public void Dispose() => _env.Dispose();

	private static DateTimeOffset Ago(TimeSpan span) => DateTimeOffset.UtcNow - span;

	private SessionNodeModel Session(string name, TimeSpan? activeAgo = null, string? supervisor = null)
	{
		var s = _env.AddSession(_dir, name, supervisorNodeId: supervisor);
		if (activeAgo.HasValue)
			_env.Activity.Set(s.NodeId, Ago(activeAgo.Value));
		return s;
	}

	private async Task<string[]> ListNamesAsync()
	{
		var result = await _env.CallAsync("recent.list");
		return result["sessions"]!.AsArray().Select(r => r!["name"]!.GetValue<string>()).ToArray();
	}

	[Fact]
	public async Task List_ShowsSessionsActiveWithin24Hours_NewestFirst_AndDropsOlderOnes()
	{
		Session("Old", TimeSpan.FromHours(25));
		Session("Mid", TimeSpan.FromHours(5));
		Session("New", TimeSpan.FromMinutes(3));
		Session("NoActivity");

		Assert.Equal(["New", "Mid"], await ListNamesAsync());
	}

	[Fact]
	public async Task List_IncludesServerTime_AndTheRowShape()
	{
		var s = Session("Row", TimeSpan.FromMinutes(1));

		var before = DateTimeOffset.UtcNow;
		var result = await _env.CallAsync("recent.list");

		Assert.True(DateTimeOffset.Parse(result["serverTimeUtc"]!.GetValue<string>()) >= before.AddSeconds(-1));
		var row = result["sessions"]![0]!;
		Assert.Equal(s.NodeId, row["id"]!.GetValue<string>());
		Assert.Equal(_dir.Path, row["directoryLabel"]!.GetValue<string>());
		Assert.Equal(s.NodeId, row["state"]!["id"]!.GetValue<string>());
		Assert.False(row["hasSchedules"]!.GetValue<bool>());
		Assert.False(row["pinned"]!.GetValue<bool>());
		Assert.True(row["canHide"]!.GetValue<bool>());
	}

	[Fact]
	public async Task List_Pinned_Favourite_StaysRegardlessOfAge_AndCannotBeHidden()
	{
		var fav = Session("Fav", TimeSpan.FromDays(30));
		fav.IsFavorite = true;

		var result = await _env.CallAsync("recent.list");

		var row = result["sessions"]!.AsArray().Single()!;
		Assert.True(row["pinned"]!.GetValue<bool>());
		Assert.False(row["canHide"]!.GetValue<bool>());
	}

	[Fact]
	public async Task List_Pinned_OpenAttentionItem_DecidedIncluded_StaysRegardlessOfAge()
	{
		var withAttention = Session("Attn", TimeSpan.FromDays(30));
		_env.Attention.SummaryByNode[withAttention.NodeId] = new AttentionSummaryModel { AllOpenCount = 1, AllOpenTopKind = AttentionKind.Decided };

		Assert.Equal(["Attn"], await ListNamesAsync());
	}

	[Fact]
	public async Task List_Pinned_Schedule_StaysRegardlessOfAge_AndFlagsHasSchedules()
	{
		var scheduled = Session("Sched", TimeSpan.FromDays(30));
		_env.Scheduler.AddSchedule(new ScheduleModel { ScheduleId = "s1", TargetNodeId = scheduled.NodeId });

		var result = await _env.CallAsync("recent.list");

		var row = result["sessions"]!.AsArray().Single()!;
		Assert.True(row["hasSchedules"]!.GetValue<bool>());
		Assert.True(row["pinned"]!.GetValue<bool>());
	}

	[Fact]
	public async Task List_PinnedSessionsWithoutActivity_AreListedLast()
	{
		var fav = Session("FavNoActivity");
		fav.IsFavorite = true;
		Session("Active", TimeSpan.FromHours(2));

		Assert.Equal(["Active", "FavNoActivity"], await ListNamesAsync());
	}

	[Fact]
	public async Task List_HiddenSession_StaysHiddenUntilNewerActivity()
	{
		var hidden = Session("Hidden", TimeSpan.FromHours(2));
		hidden.RecentIgnoredAt = DateTimeOffset.UtcNow;
		var revived = Session("Revived", TimeSpan.FromMinutes(1));
		revived.RecentIgnoredAt = DateTimeOffset.UtcNow.AddHours(-1);   // activity (1 min ago) is newer than the hide

		Assert.Equal(["Revived"], await ListNamesAsync());
	}

	[Fact]
	public async Task List_HiddenButPinned_StaysListed()
	{
		var fav = Session("FavHidden", TimeSpan.FromHours(2));
		fav.IsFavorite = true;
		fav.RecentIgnoredAt = DateTimeOffset.UtcNow;

		Assert.Equal(["FavHidden"], await ListNamesAsync());
	}

	[Fact]
	public async Task List_CandidatesAreTopLevelSessionsOnly_WorkersAreNotListed_OrphansAre()
	{
		var sup = Session("Sup", TimeSpan.FromMinutes(5));
		Session("Worker", TimeSpan.FromMinutes(1), sup.NodeId);
		Session("Orphan", TimeSpan.FromMinutes(2), "missing");

		Assert.Equal(["Orphan", "Sup"], await ListNamesAsync());
	}

	[Fact]
	public async Task List_LabelIsTheSessionsDirectoryLabel_AcrossDirectories()
	{
		var other = _env.AddDirectory("other");
		var s = _env.AddSession(other, "InOther");
		_env.Activity.Set(s.NodeId, Ago(TimeSpan.FromMinutes(1)));

		var result = await _env.CallAsync("recent.list");

		Assert.Equal(other.Path, result["sessions"]![0]!["directoryLabel"]!.GetValue<string>());
	}

	[Fact]
	public async Task List_ExactlyOnTheBoundary_24Hours_IsStillListed()
	{
		Session("Edge", TimeSpan.FromHours(23.99));

		Assert.Equal(["Edge"], await ListNamesAsync());
	}

	// ── recent.hide ───────────────────────────────────────────────────────────────

	[Fact]
	public async Task Hide_SetsRecentIgnoredAt_PersistsIt_AndRemovesTheRowFromTheList()
	{
		var s = Session("S", TimeSpan.FromHours(1));
		var before = DateTimeOffset.UtcNow;

		var result = await _env.CallAsync("recent.hide", $$"""{"nodeId":"{{s.NodeId}}"}""");

		Assert.True(result["hidden"]!.GetValue<bool>());
		Assert.NotNull(s.RecentIgnoredAt);
		Assert.True(s.RecentIgnoredAt >= before);
		Assert.Empty(await ListNamesAsync());

		var reloaded = new AppSettingsService(_env.Root);
		reloaded.Load();
		Assert.NotNull(reloaded.Settings.Tree.Single(d => d.NodeId == _dir.NodeId).Sessions.Single(x => x.NodeId == s.NodeId).RecentIgnoredAt);
	}

	[Fact]
	public async Task Hide_RaisesSessionSettingsChanged_SoTheDesktopRowRefreshes_AndAPinnedRefusalDoesNot()
	{
		var raised = new List<string>();
		_env.Registry.SessionSettingsChanged += (_, e) => raised.Add(e.NodeId);
		var s = Session("S", TimeSpan.FromHours(1));
		var fav = Session("Fav", TimeSpan.FromHours(1));
		fav.IsFavorite = true;

		await _env.CallAsync("recent.hide", $$"""{"nodeId":"{{s.NodeId}}"}""");
		await _env.CallAsync("recent.hide", $$"""{"nodeId":"{{fav.NodeId}}"}""");

		Assert.Equal([s.NodeId], raised);
	}

	[Fact]
	public async Task Hide_RefusesAPinnedSession_AndChangesNothing()
	{
		var fav = Session("Fav", TimeSpan.FromHours(1));
		fav.IsFavorite = true;
		var attn = Session("Attn", TimeSpan.FromHours(1));
		_env.Attention.SummaryByNode[attn.NodeId] = new AttentionSummaryModel { AllOpenCount = 1, AllOpenTopKind = AttentionKind.Todo };
		var sched = Session("Sched", TimeSpan.FromHours(1));
		_env.Scheduler.AddSchedule(new ScheduleModel { ScheduleId = "s1", TargetNodeId = sched.NodeId });

		foreach (var s in new[] { fav, attn, sched })
		{
			var result = await _env.CallAsync("recent.hide", $$"""{"nodeId":"{{s.NodeId}}"}""");
			Assert.False(result["hidden"]!.GetValue<bool>());
			Assert.Null(s.RecentIgnoredAt);
		}
	}

	[Fact]
	public async Task Hide_UnknownSession_IsSessionNotFound_AndMissingParamIsInvalid()
	{
		var unknown = await _env.CallRawAsync("recent.hide", """{"nodeId":"nope"}""");
		var missing = await _env.CallRawAsync("recent.hide");

		Assert.Equal(-32000, RemoteTreeTestEnv.ErrorCode(unknown));
		Assert.Equal(-32602, RemoteTreeTestEnv.ErrorCode(missing));
	}
}
