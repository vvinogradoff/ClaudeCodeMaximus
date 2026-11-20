using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>Fixes of the W3 review: H1 (Recent re-sort and age-out), H2 (Tags scope), H3 (diffing), H4 (Recent tab from the tree), H5 (row content), H7, H8, H10.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionListsReviewFixTests
{
	private sealed class Actions : ISessionListActions
	{
		public List<string> Opened { get; } = [];

		public void Open(string nodeId, string name) => Opened.Add(nodeId);

		public Task<bool?> SetFavoriteAsync(string nodeId, bool value) => Task.FromResult<bool?>(value);
	}

	private sealed class MemoryState : IClientStateStore
	{
		public ClientStateModel State { get; } = new();

		public Task LoadAsync() => Task.CompletedTask;

		public void Update(Action<ClientStateModel> change) => change(State);

		public Task FlushAsync() => Task.CompletedTask;
	}

	private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

	private readonly FakeTessynClient _client = new();
	private readonly TreeBuilder _b = new();
	private readonly Actions _actions = new();
	private readonly ImmediateUiDispatcher _ui = new();

	private static SessionStateDto State(string id, bool running = false, bool favorite = false, DateTimeOffset? activity = null, bool draft = false, DateTimeOffset? prompt = null, string? profile = null, int attention = 0) =>
		new(id, running, false, attention > 0 ? new RowAttentionDto(attention, AttentionKind.Ask) : null, favorite, activity, prompt, draft, profile);

	private async Task<TreeViewModel> LoadTreeAsync(params TreeDirectoryDto[] directories)
	{
		_client.Directories = directories.ToList();
		var tree = new TreeViewModel(_client, _ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		return tree;
	}

	private RecentTabViewModel CreateTab() => new(_actions, _ui, NullLogger<RecentTabViewModel>.Instance, TimeSpan.Zero);

	private static List<string> Shown(GroupedSessionListViewModel list) =>
		list.Items.Select(i => i switch { SessionGroupHeaderViewModel h => "[" + h.DirectoryId + "]", SessionRowViewModel r => r.Id, _ => "?" }).ToList();

	// ---- H4: the Recent tab is every top-level session per directory, built from the tree

	[Fact]
	public async Task RecentTab_ListsEveryTopLevelSessionPerDirectory_DraftsFirstThenNewest_WorkersAndOldSessionsIncluded()
	{
		var tree = await LoadTreeAsync(
			_b.Directory("d1", "Repo", [_b.Group("g1", "G", _b.SessionWith(State("s4", activity: Now.AddDays(-30)), "Ancient"))],
				_b.SessionWith(State("s1", activity: Now.AddHours(-3)), "Old"), _b.SessionWith(State("s2", activity: Now.AddMinutes(-1)), "New"), _b.SessionWith(State("s3", draft: true, activity: Now.AddDays(-2)), "Draft")),
			_b.Directory("d2", "Other", [], _b.SessionWith(State("s5"), "Solo")));
		using var tab = CreateTab();

		tab.Attach(tree);

		Assert.Equal(["[d1]", "s3", "s2", "s1", "s4", "[d2]", "s5"], Shown(tab.List));
		Assert.Equal("Draft", tab.List.Items.OfType<SessionRowViewModel>().First().Name);
		Assert.False(tab.List.Items.OfType<SessionRowViewModel>().First().ShowDirectory);
	}

	// TC.RT.13 (FR.9.20): a Focus reply draft moves a session's last activity but is not a composer draft: the row has no pencil and does not jump ahead of the composer drafts.
	[Fact]
	public async Task RecentTab_AReplyDraftSession_IsNewestButNotADraft_SoComposerDraftsStayFirst()
	{
		var tree = await LoadTreeAsync(
			_b.Directory("d1", "Repo", [],
				_b.SessionWith(State("s1", activity: Now.AddHours(-3)), "Old"),
				_b.SessionWith(State("s2", activity: Now.AddMinutes(-1), draft: false, prompt: Now.AddDays(-2)), "ReplyDraft"),
				_b.SessionWith(State("s3", draft: true, activity: Now.AddHours(-5)), "Composer")));
		using var tab = CreateTab();

		tab.Attach(tree);

		Assert.Equal(["[d1]", "s3", "s2", "s1"], Shown(tab.List));
		var reply = tab.List.Items.OfType<SessionRowViewModel>().Single(r => r.Id == "s2");
		Assert.False(reply.HasDraft);

		// The 15-minute colour comes from the newest activity (the reply draft), not from the old prompt.
		var timed = new SessionRowViewModel("s2", "ReplyDraft", "d1", reply.State, clock: new FixedClock(Now));
		Assert.Equal(4, timed.RecencyLevel);
		Assert.False(timed.HasDraft);
	}

	[Fact]
	public async Task RecentTab_LeavesOutWorkers()
	{
		_client.Directories = [_b.Directory("d1", "Repo", [], _b.Session("s1", "Top", workers: 1))];
		_client.Workers["s1"] = [_b.Session("w1", "Worker", supervisor: "s1")];
		var tree = new TreeViewModel(_client, _ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows.Single(r => r.Id == "d1"));
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows.Single(r => r.Id == "s1"));
		using var tab = CreateTab();

		tab.Attach(tree);

		Assert.Equal(["[d1]", "s1"], Shown(tab.List));
	}

	[Fact]
	public async Task RecentTab_Activate_CollapsesEveryGroupButTheSelectedSessionsDirectory_AndManualToggleHolds()
	{
		var tree = await LoadTreeAsync(
			_b.Directory("d1", "Repo", [], _b.Session("s1", "One")),
			_b.Directory("d2", "Other", [], _b.Session("s2", "Two")),
			_b.Directory("d3", "Third", [], _b.Session("s3", "Three")));
		using var tab = CreateTab();
		tab.Attach(tree);
		tree.SelectNodeById("s2");

		tab.Activate();

		Assert.Equal(["[d1]", "[d2]", "s2", "[d3]"], Shown(tab.List));

		var first = tab.List.Items.OfType<SessionGroupHeaderViewModel>().First();
		tab.List.ToggleGroupCommand.Execute(first);
		Assert.Equal(["[d1]", "s1", "[d2]", "s2", "[d3]"], Shown(tab.List));

		tree.SelectNodeById("s1");
		tab.Rebuild();   // a live rebuild keeps what the user did
		Assert.Equal(["[d1]", "s1", "[d2]", "s2", "[d3]"], Shown(tab.List));

		tab.Activate();
		Assert.Equal(["[d1]", "s1", "[d2]", "[d3]"], Shown(tab.List));
	}

	[Fact]
	public async Task RecentPanel_OnPanelSelected_ActsLikeShowingTheTab_CollapsingEveryGroupButTheSelectedSessionsOne()
	{
		var tree = await LoadTreeAsync(
			_b.Directory("d1", "Repo", [], _b.Session("s1", "One")),
			_b.Directory("d2", "Other", [], _b.Session("s2", "Two")));
		using var tab = CreateTab();
		tab.Attach(tree);
		tree.SelectNodeById("s2");

		var shown = new List<string>();
		StaTestRunner.Run(() =>
		{
			var panel = new TessynDesktop.WPF.Views.RecentPanel { DataContext = tab };
			Assert.IsAssignableFrom<TessynDesktop.WPF.Views.IPanelSelectionAware>(panel);
			tab.List.ToggleGroupCommand.Execute(tab.List.Items.OfType<SessionGroupHeaderViewModel>().First());   // the user opened d1 earlier
			panel.OnPanelSelected();                                                                            // the panel became the selected tab of its pane (FR.1.16)
			shown.AddRange(Shown(tab.List));
		});

		Assert.Equal(["[d1]", "[d2]", "s2"], shown);
	}

	[Fact]
	public async Task RecentTab_Activate_WithNoSelectionExpandsEverything()
	{
		var tree = await LoadTreeAsync(_b.Directory("d1", "Repo", [], _b.Session("s1", "One")), _b.Directory("d2", "Other", [], _b.Session("s2", "Two")));
		using var tab = CreateTab();
		tab.Attach(tree);
		tab.List.ToggleGroupCommand.Execute(tab.List.Items.OfType<SessionGroupHeaderViewModel>().First());
		Assert.Equal(["[d1]", "[d2]", "s2"], Shown(tab.List));

		tab.Activate();

		Assert.Equal(["[d1]", "s1", "[d2]", "s2"], Shown(tab.List));
	}

	[Fact]
	public async Task RecentTab_FollowsTheTreesStates_AndKeepsTheRowObjects()
	{
		var tree = await LoadTreeAsync(_b.Directory("d1", "Repo", [], _b.SessionWith(State("s1", activity: Now.AddHours(-2)), "A"), _b.SessionWith(State("s2", activity: Now.AddHours(-3)), "B")));
		using var tab = CreateTab();
		tab.Attach(tree);
		var rowB = tab.List.Items.OfType<SessionRowViewModel>().Single(r => r.Id == "s2");
		Assert.Equal(["[d1]", "s1", "s2"], Shown(tab.List));

		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(State("s2", activity: Now, draft: false, running: true)));

		Assert.Equal(["[d1]", "s2", "s1"], Shown(tab.List));
		Assert.Same(rowB, tab.List.Items.OfType<SessionRowViewModel>().First());
		Assert.True(rowB.IsRunning);
	}

	[Fact]
	public async Task RecentTab_OpenAndFavourite_GoThroughTheActions()
	{
		var tree = await LoadTreeAsync(_b.Directory("d1", "Repo", [], _b.Session("s1", "One")));
		using var tab = CreateTab();
		tab.Attach(tree);
		var row = tab.List.Items.OfType<SessionRowViewModel>().Single();

		tab.OpenCommand.Execute(row);
		tab.ToggleFavoriteCommand.Execute(row);
		await Task.Yield();

		Assert.Equal(["s1"], _actions.Opened);
		Assert.True(row.IsFavorite);
	}

	// ---- H5: row content

	[Fact]
	public void Row_ShowsDraftProfileLastPromptAndRecency()
	{
		var clock = new FixedClock(Now);
		var row = new SessionRowViewModel("s", "N", "dir", State("s", draft: true, profile: "work", prompt: Now.AddMinutes(-10), attention: 2), clock: clock);

		Assert.True(row.HasDraft);
		Assert.Equal("work", row.ProfileName);
		Assert.Equal(2, row.AttentionCount);
		Assert.NotEqual(string.Empty, row.LastPromptText);

		// Recency tiers as the Avalonia client: 15 minutes, 1 hour, 4 hours, 12 hours (4 .. 1); older or none is 0, a draft keeps the palest tier.
		Assert.Equal(4, row.RecencyLevel);
		Assert.Equal(3, new SessionRowViewModel("s", "N", "d", State("s", prompt: Now.AddMinutes(-20)), clock: clock).RecencyLevel);
		Assert.Equal(3, new SessionRowViewModel("s", "N", "d", State("s", prompt: Now.AddMinutes(-45)), clock: clock).RecencyLevel);
		Assert.Equal(2, new SessionRowViewModel("s", "N", "d", State("s", prompt: Now.AddMinutes(-90)), clock: clock).RecencyLevel);
		Assert.Equal(1, new SessionRowViewModel("s", "N", "d", State("s", prompt: Now.AddHours(-6)), clock: clock).RecencyLevel);
		Assert.Equal(0, new SessionRowViewModel("s", "N", "d", State("s", prompt: Now.AddHours(-13)), clock: clock).RecencyLevel);
		Assert.Equal(1, new SessionRowViewModel("s", "N", "d", State("s", draft: true, prompt: Now.AddHours(-30)), clock: clock).RecencyLevel);
		Assert.Equal(0, new SessionRowViewModel("s", "N", "d", State("s"), clock: clock).RecencyLevel);
		Assert.Equal(1, new SessionRowViewModel("s", "N", "d", State("s", draft: true), clock: clock).RecencyLevel);

		// The dotted border marks the last 24 hours.
		Assert.True(new SessionRowViewModel("s", "N", "d", State("s", prompt: Now.AddHours(-23)), clock: clock).IsRecentlyActive);
		Assert.False(new SessionRowViewModel("s", "N", "d", State("s", prompt: Now.AddHours(-25)), clock: clock).IsRecentlyActive);
	}

	[Fact]
	public void Row_IconSlotShowsOneIcon_StarThenHideThenStarToSet()
	{
		var favourite = new SessionRowViewModel("a", "A", "d", State("a", favorite: true), canHide: false, canToggleFavorite: true);
		var hideable = new SessionRowViewModel("b", "B", "d", State("b"), canHide: true);
		var tabRow = new SessionRowViewModel("c", "C", "d", State("c"), canToggleFavorite: true);
		var plain = new SessionRowViewModel("d", "D", "d", State("d"));

		Assert.Equal((true, false, false), (favourite.ShowStar, favourite.ShowHide, favourite.ShowStarToggle));
		Assert.Equal((false, true, false), (hideable.ShowStar, hideable.ShowHide, hideable.ShowStarToggle));
		Assert.Equal((false, false, true), (tabRow.ShowStar, tabRow.ShowHide, tabRow.ShowStarToggle));
		Assert.Equal((false, false, false), (plain.ShowStar, plain.ShowHide, plain.ShowStarToggle));
		var pinned = new SessionRowViewModel("e", "E", "d", State("e", favorite: true), canHide: true);
		Assert.False(pinned.ShowHide);   // a favourite is never hidden: the star takes the slot
	}

	[Fact]
	public void Row_RefreshClock_RaisesTheRecencyChange()
	{
		var row = new SessionRowViewModel("s", "N", "d", State("s", prompt: Now.AddMinutes(-14)), clock: new FixedClock(Now));
		var raised = new List<string?>();
		row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

		row.RefreshClock();

		Assert.Contains(nameof(SessionRowViewModel.RecencyLevel), raised);
	}

	private sealed class FixedClock : TimeProvider
	{
		private readonly DateTimeOffset _now;

		public FixedClock(DateTimeOffset now) => _now = now;

		public override DateTimeOffset GetUtcNow() => _now;

		public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
	}

	// ---- H1: the dropdown list re-sorts, ages out and follows pins

	private static RecentRowDto Row(string id, DateTimeOffset activity, bool pinned = false, int attention = 0, bool favorite = false) =>
		new(id, id.ToUpperInvariant(), "proj", State(id, activity: activity, attention: attention, favorite: favorite), false, pinned, !pinned);

	private RecentViewModel CreateRecent(TimeSpan? throttle = null) => new(_actions, _ui, NullLogger<RecentViewModel>.Instance, throttle ?? TimeSpan.Zero);

	[Fact]
	public async Task Recent_APromptInAnotherSession_ReSortsTheList()
	{
		var rows = new List<RecentRowDto> { Row("a", Now), Row("b", Now.AddMinutes(-5)), Row("c", Now.AddMinutes(-10)) };
		_client.RecentListHandler = () => new RecentListResult(Now, rows.OrderByDescending(r => r.State.LastActivityUtc).ToList());
		using var recent = CreateRecent();
		await recent.AttachAsync(_client);
		Assert.Equal(["a", "b", "c"], recent.Rows.Select(r => r.Id));

		var updated = Row("c", Now.AddMinutes(1));
		rows[2] = updated;
		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(updated.State));
		await recent.RefreshTask;

		Assert.Equal(["c", "a", "b"], recent.Rows.Select(r => r.Id));
	}

	[Fact]
	public async Task Recent_ARowThatLeftTheWindow_AgesOutOnTheNextReload()
	{
		var rows = new List<RecentRowDto> { Row("a", Now), Row("old", Now.AddHours(-23)) };
		_client.RecentListHandler = () => new RecentListResult(Now, rows.ToList());
		using var recent = CreateRecent();
		await recent.AttachAsync(_client);

		rows.RemoveAt(1);
		await recent.RefreshAsync();   // what opening the dropdown does

		Assert.Equal(["a"], recent.Rows.Select(r => r.Id));
	}

	[Fact]
	public async Task Recent_ANewFocusItemPinsARow_TheHideButtonGoesAway()
	{
		var rows = new List<RecentRowDto> { Row("a", Now) };
		_client.RecentListHandler = () => new RecentListResult(Now, rows.ToList());
		using var recent = CreateRecent();
		await recent.AttachAsync(_client);
		Assert.True(recent.Rows[0].ShowHide);

		rows[0] = Row("a", Now, pinned: true, attention: 1);
		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(rows[0].State));
		await recent.RefreshTask;

		Assert.False(recent.Rows[0].CanHide);
		Assert.False(recent.Rows[0].ShowHide);
	}

	[Fact]
	public async Task Recent_WhileATurnStreams_ReloadsAreThrottled()
	{
		var rows = new List<RecentRowDto> { Row("a", Now), Row("b", Now.AddMinutes(-1)) };
		_client.RecentListHandler = () => new RecentListResult(Now, rows.ToList());
		using var recent = CreateRecent(TimeSpan.FromMilliseconds(300));
		await recent.AttachAsync(_client);
		var baseline = _client.Calls.Count(c => c == "recent.list");

		for (var i = 1; i <= 30; i++)
			_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(State("b", activity: Now.AddSeconds(i))));
		await Task.Delay(700);

		var extra = _client.Calls.Count(c => c == "recent.list") - baseline;
		Assert.InRange(extra, 1, 3);
	}

	// ---- H8: stale results

	[Fact]
	public async Task Recent_ASecondClientAttachedDuringALoad_ShowsOnlyTheSecondClientsAnswer()
	{
		var gate = new TaskCompletionSource();
		var old = new FakeTessynClient { RecentListHandler = () => new RecentListResult(Now, [Row("old", Now)]), RecentListGate = gate.Task };
		var fresh = new FakeTessynClient { RecentListHandler = () => new RecentListResult(Now, [Row("new", Now)]) };
		using var recent = CreateRecent();

		var first = recent.AttachAsync(old);
		var second = recent.AttachAsync(fresh);
		gate.SetResult();
		await first;
		await second;
		await recent.RefreshTask;

		Assert.Equal(["new"], recent.Rows.Select(r => r.Id));
		Assert.Null(recent.Error);
	}

	[Fact]
	public async Task Recent_ALoadThatWasRunningWhenARowWasHidden_DoesNotBringItBack()
	{
		_client.RecentListHandler = () => new RecentListResult(Now, [Row("a", Now), Row("b", Now.AddMinutes(-1))]);
		using var recent = CreateRecent();
		await recent.AttachAsync(_client);
		var gate = new TaskCompletionSource();
		_client.RecentListGate = gate.Task;
		var reload = recent.RefreshAsync();   // the dropdown just opened: a load that still lists "a"

		await recent.HideCommand.ExecuteAsync(recent.Rows[0]);
		gate.SetResult();
		await reload;

		Assert.Equal(["b"], recent.Rows.Select(r => r.Id));
	}

	// ---- H2: Tags scope

	private static TagSearchResult OneChip(bool scopedEmpty = false) =>
		scopedEmpty ? new TagSearchResult(TagEmptyReason.NoTagsScoped, [], TagSessionsEmptyReason.SelectTags, [])
			: new TagSearchResult(null, [new TagChipDto("a", "A", "https://x/a", SessionTagKind.LinearIssue, 1, false)], TagSessionsEmptyReason.SelectTags, []);

	private TagSearchViewModel CreateTags() => new(_actions, _ui, NullLogger<TagSearchViewModel>.Instance, TimeSpan.Zero);

	[Fact]
	public async Task Tags_NoSelection_SearchesEverythingAndTheScopeIsForcedOff()
	{
		var tree = await LoadTreeAsync(_b.Directory("d1", "Repo", [], _b.Session("s1", "One")));
		using var tags = CreateTags();
		tags.AttachTree(tree);

		await tags.AttachAsync(_client);

		Assert.Null(_client.TagsSearchScopes.Last());
		Assert.False(tags.CanScope);
		Assert.False(tags.IsScoped);
		Assert.True(tags.IsScopeEnabled);   // the toggle itself stays on by default
	}

	[Fact]
	public async Task Tags_ASelectedSession_ScopesTheSearchToItByDefault()
	{
		var tree = await LoadTreeAsync(_b.Directory("d1", "Repo", [], _b.Session("s1", "One")), _b.Directory("d2", "Other", [], _b.Session("s2", "Two")));
		using var tags = CreateTags();
		tags.AttachTree(tree);
		await tags.AttachAsync(_client);

		tree.SelectNodeById("s1");
		await tags.SearchTask;

		Assert.Equal("s1", _client.TagsSearchScopes.Last());
		Assert.True(tags.IsScoped);
	}

	[Fact]
	public async Task Tags_ASelectedWorker_ScopesToItsSupervisorsDirectory()
	{
		_client.Directories = [_b.Directory("d1", "Repo", [], _b.Session("s1", "Top", workers: 1))];
		_client.Workers["s1"] = [_b.Session("w1", "Worker", supervisor: "s1")];
		var tree = new TreeViewModel(_client, _ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows.Single(r => r.Id == "d1"));
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows.Single(r => r.Id == "s1"));
		using var tags = CreateTags();
		tags.AttachTree(tree);
		await tags.AttachAsync(_client);

		tree.SelectNodeById("w1");
		await tags.SearchTask;

		Assert.Equal("w1", _client.TagsSearchScopes.Last());   // the daemon resolves a worker to its supervisor's directory
		Assert.Equal("d1", tree.DirectoryIdOf("w1"));
	}

	[Fact]
	public async Task Tags_TheScopeToggleSearchesAgain_AndOffMeansEverything()
	{
		var tree = await LoadTreeAsync(_b.Directory("d1", "Repo", [], _b.Session("s1", "One")));
		using var tags = CreateTags();
		tags.AttachTree(tree);
		await tags.AttachAsync(_client);
		tree.SelectNodeById("s1");
		await tags.SearchTask;

		tags.IsScopeEnabled = false;
		await tags.SearchTask;

		Assert.Null(_client.TagsSearchScopes.Last());
		Assert.False(tags.IsScoped);

		tags.IsScopeEnabled = true;
		await tags.SearchTask;
		Assert.Equal("s1", _client.TagsSearchScopes.Last());
	}

	[Fact]
	public async Task Tags_MovingToAnotherDirectorySearchesAgain_ButWithinTheSameDirectoryItDoesNot()
	{
		var tree = await LoadTreeAsync(_b.Directory("d1", "Repo", [], _b.Session("s1", "One"), _b.Session("s1b", "OneB")), _b.Directory("d2", "Other", [], _b.Session("s2", "Two")));
		using var tags = CreateTags();
		tags.AttachTree(tree);
		await tags.AttachAsync(_client);
		tree.SelectNodeById("s1");
		await tags.SearchTask;
		var count = _client.TagsSearchScopes.Count;

		tree.SelectNodeById("s1b");
		await tags.SearchTask;
		Assert.Equal(count, _client.TagsSearchScopes.Count);

		tree.SelectNodeById("s2");
		await tags.SearchTask;
		Assert.Equal(count + 1, _client.TagsSearchScopes.Count);
		Assert.Equal("s2", _client.TagsSearchScopes.Last());
	}

	[Fact]
	public async Task Tags_ASelectedDirectory_IsNotScoped_AsInAvalonia()
	{
		var tree = await LoadTreeAsync(_b.Directory("d1", "Repo", [], _b.Session("s1", "One")));
		using var tags = CreateTags();
		tags.AttachTree(tree);
		await tags.AttachAsync(_client);

		tree.SelectNodeById("d1");
		await tags.SearchTask;

		Assert.Null(_client.TagsSearchScopes.Last());   // FR.1.10 / FR.24.11: the project scope belongs to a selected session; a directory selected means every tag is searched
		Assert.False(tags.CanScope);
	}

	[Fact]
	public async Task Tags_NoTagsInTheScope_SaysSoWithTheDirectoryText()
	{
		var tree = await LoadTreeAsync(_b.Directory("d1", "Repo", [], _b.Session("s1", "One")));
		_client.TagsSearchHandler = (_, _) => OneChip(scopedEmpty: true);
		using var tags = CreateTags();
		tags.AttachTree(tree);
		await tags.AttachAsync(_client);
		tree.SelectNodeById("s1");
		await tags.SearchTask;

		Assert.Equal(Constants.SessionLists.TagsNoTagsScopedText, tags.EmptyChipsText);
	}

	// ---- H3: in-place updates

	[Fact]
	public async Task Tags_ANewSearchUpdatesTheChipObjectsInPlace()
	{
		var count = 1;
		_client.TagsSearchHandler = (_, _) => new TagSearchResult(null, [new TagChipDto("a", "A", "u", SessionTagKind.LinearIssue, count, false)], TagSessionsEmptyReason.SelectTags, []);
		using var tags = CreateTags();
		await tags.AttachAsync(_client);
		var chip = tags.Chips[0];

		count = 7;
		await tags.RefreshAsync();

		Assert.Same(chip, tags.Chips[0]);
		Assert.Equal("A (7)", chip.Text);
		Assert.Contains("In 7 sessions", chip.ToolTipText);
	}

	[Fact]
	public async Task Tags_SessionRowsAreKeptAcrossSearches_AndGroupsCollapseByDirectoryAndStayCollapsed()
	{
		_client.TagsSearchHandler = (_, sel) => new TagSearchResult(null, [new TagChipDto("a", "A", "u", SessionTagKind.LinearIssue, 2, sel.Count > 0)], null,
			[new SessionGroupDto("d1", "Repo", [new SessionRowDto("s1", "One", State("s1")), new SessionRowDto("s2", "Two", State("s2"))])]);
		using var tags = CreateTags();
		await tags.AttachAsync(_client);
		var row = tags.Sessions.Items.OfType<SessionRowViewModel>().First();
		Assert.False(row.ShowDirectory);

		tags.Sessions.ToggleGroupCommand.Execute(tags.Sessions.Items.OfType<SessionGroupHeaderViewModel>().Single());
		await tags.RefreshAsync();

		Assert.Equal(["[d1]"], Shown(tags.Sessions));
		tags.Sessions.ToggleGroupCommand.Execute(tags.Sessions.Items.OfType<SessionGroupHeaderViewModel>().Single());
		Assert.Same(row, tags.Sessions.Items.OfType<SessionRowViewModel>().First());
	}

	[Fact]
	public void ListDiff_KeepsObjectsAndOnlyMovesWhatChanged()
	{
		var diff = new ListDiff();
		var (a, b, c, d) = (new object(), new object(), new object(), new object());
		var list = new System.Collections.ObjectModel.ObservableCollection<object> { a, b, c };
		var events = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
		list.CollectionChanged += (_, e) => events.Add(e.Action);

		diff.Apply(list, [c, a, d]);

		Assert.Equal([c, a, d], list.ToArray());
		Assert.DoesNotContain(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, events);
		Assert.True(events.Count <= 4);
	}

	[Fact]
	public async Task GroupedList_HandlesSeveralHundredRowsWithoutRebuildingTheItems()
	{
		var rows = Enumerable.Range(0, 600).Select(i => _b.SessionWith(State("s" + i, activity: Now.AddMinutes(-i)), "S" + i)).ToArray();
		var tree = await LoadTreeAsync(_b.Directory("d1", "Repo", [], rows));
		using var tab = CreateTab();
		tab.Attach(tree);
		var first = tab.List.Items[1];
		var resets = 0;
		tab.List.Items.CollectionChanged += (_, e) =>
		{
			if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
				resets++;
		};

		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(State("s300", activity: Now.AddHours(1))));

		Assert.Equal(601, tab.List.Items.Count);
		Assert.Equal(0, resets);
		Assert.Equal("s300", ((SessionRowViewModel)tab.List.Items[1]).Id);
		Assert.Same(first, tab.List.Items[2]);
	}

	// ---- H7

	[Fact]
	public async Task Tags_OpenInBrowserOpensTheChipsLinkThroughTheLauncher()
	{
		var launcher = new FakeSystemUrlLauncher();
		_client.TagsSearchHandler = (_, _) => OneChip();
		using var tags = new TagSearchViewModel(_actions, _ui, NullLogger<TagSearchViewModel>.Instance, TimeSpan.Zero, launcher);
		await tags.AttachAsync(_client);

		tags.OpenChipCommand.Execute(tags.Chips[0]);

		Assert.Equal(["https://x/a"], launcher.Opened);
	}

	// ---- H8: a superseded failure and the header star

	[Fact]
	public async Task Tags_AFailureOfASupersededSearchIsNotShown()
	{
		var first = true;
		_client.TagsSearchHandler = (_, _) =>
		{
			if (first)
			{
				first = false;
				throw new ObjectDisposedException("teardown");
			}

			return OneChip();
		};
		using var tags = CreateTags();

		await tags.AttachAsync(_client);   // the first call fails and is genuinely shown ...
		Assert.NotNull(tags.Error);
		await tags.RefreshAsync();

		Assert.Null(tags.Error);   // ... and the next successful one clears it
	}

	// ---- H10: attach order

	[Fact]
	public async Task Host_CreatedAfterTheClientIsReady_AttachesAtOnce()
	{
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.Found, new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "t", "r", 1)));
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(_client), _ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		var main = new MainViewModel(connection, c => new TreeViewModel(c, _ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero),
			c => new SessionPaneViewModel(c, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), _ui,
				new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance),
			new MemoryState(), NullLogger<MainViewModel>.Instance);
		_client.Directories = [_b.Directory("d1", "Repo", [], _b.Session("s1", "One"))];
		await main.StartAsync(CancellationToken.None);   // the client is ready before the host exists
		await main.Tree!.WhenIdleAsync();

		using var host = new SessionListsHost(connection, main, _ui, NullLoggerFactory.Instance);

		Assert.Contains("recent.list", _client.Calls);
		Assert.Contains("tags.search::", _client.Calls);
		Assert.Contains("s1", host.RecentTab.List.Items.OfType<SessionRowViewModel>().Select(r => r.Id));
	}

	[Fact]
	public async Task Host_OpenWithNoSessionPane_LogsAndDoesNothing()
	{
		var locator = new ScriptedLocator();
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(_client), _ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		var main = new MainViewModel(connection, c => new TreeViewModel(c, _ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero),
			c => throw new InvalidOperationException(), new MemoryState(), NullLogger<MainViewModel>.Instance);
		var log = new CapturingLogger<SessionListsHost>();
		using var host = new SessionListsHost(connection, main, _ui, new SingleLoggerFactory(log));

		host.Open("s1", "One");

		Assert.Contains(log.Messages, m => m.Contains("Cannot open", StringComparison.Ordinal));
		await Task.CompletedTask;
	}

	private sealed class SingleLoggerFactory : Microsoft.Extensions.Logging.ILoggerFactory
	{
		private readonly Microsoft.Extensions.Logging.ILogger _host;

		public SingleLoggerFactory(Microsoft.Extensions.Logging.ILogger host) => _host = host;

		public void AddProvider(Microsoft.Extensions.Logging.ILoggerProvider provider)
		{
		}

		public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
			categoryName.EndsWith(nameof(SessionListsHost), StringComparison.Ordinal) ? _host : NullLogger.Instance;

		public void Dispose()
		{
		}
	}
}
