using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The Recent list (FR.9.6-9.8): order, hide, pinned favourites, open, and refresh on session.changed (W3).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class RecentViewModelTests
{
	private sealed class Actions : ISessionListActions
	{
		public List<string> Opened { get; } = [];

		public Func<string, bool, bool?> SetFavorite { get; set; } = (_, value) => value;

		public void Open(string nodeId, string name) => Opened.Add(nodeId + ":" + name);

		public Task<bool?> SetFavoriteAsync(string nodeId, bool value) => Task.FromResult(SetFavorite(nodeId, value));
	}

	private readonly FakeTessynClient _client = new();
	private readonly Actions _actions = new();

	private static SessionStateDto State(string id, bool running = false, bool favorite = false) =>
		new(id, running, false, null, favorite, DateTimeOffset.UtcNow, null, false, null);

	private static RecentRowDto Row(string id, string name, bool pinned = false, bool running = false, bool favorite = false) =>
		new(id, name, "proj", State(id, running, favorite), false, pinned, !pinned);

	private RecentViewModel Create() => new(_actions, new ImmediateUiDispatcher(), NullLogger<RecentViewModel>.Instance);

	private void Serve(params RecentRowDto[] rows) => _client.RecentListHandler = () => new RecentListResult(DateTimeOffset.UtcNow, rows);

	[Fact]
	public async Task Attach_LoadsTheListInTheDaemonsOrder_PinnedRowsStayWhereTheDaemonPutsThem()
	{
		Serve(Row("fav", "Favourite one", pinned: true, favorite: true), Row("a", "Newest"), Row("b", "Older", running: true));
		using var recent = Create();

		await recent.AttachAsync(_client);

		Assert.Equal(["fav", "a", "b"], recent.Rows.Select(r => r.Id));
		Assert.True(recent.Rows[0].IsFavorite);
		Assert.False(recent.Rows[0].CanHide);
		Assert.True(recent.Rows[2].IsRunning);
		Assert.Equal("proj", recent.Rows[1].DirectoryLabel);
		Assert.False(recent.IsEmpty);
	}

	[Fact]
	public async Task EmptyList_IsReportedAsEmptyOnlyAfterTheFirstLoad()
	{
		using var recent = Create();
		Assert.False(recent.IsEmpty);

		Serve();
		await recent.AttachAsync(_client);

		Assert.True(recent.IsEmpty);
	}

	[Fact]
	public async Task Hide_CallsRecentHideAndRemovesTheRow()
	{
		Serve(Row("a", "A"), Row("b", "B"));
		using var recent = Create();
		await recent.AttachAsync(_client);

		await recent.HideCommand.ExecuteAsync(recent.Rows[0]);

		Assert.Contains("recent.hide:a", _client.Calls);
		Assert.Equal(["b"], recent.Rows.Select(r => r.Id));
	}

	[Fact]
	public async Task Hide_OfAPinnedRowDoesNothing()
	{
		Serve(Row("fav", "Pinned", pinned: true, favorite: true));
		using var recent = Create();
		await recent.AttachAsync(_client);

		await recent.HideCommand.ExecuteAsync(recent.Rows[0]);

		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("recent.hide", StringComparison.Ordinal));
		Assert.Single(recent.Rows);
	}

	[Fact]
	public async Task Hide_RefusedByTheDaemon_ReloadsTheList()
	{
		Serve(Row("a", "A"));
		_client.RecentHideHandler = _ => new RecentHideResult(false);
		using var recent = Create();
		await recent.AttachAsync(_client);

		await recent.HideCommand.ExecuteAsync(recent.Rows[0]);

		Assert.Equal(2, _client.Calls.Count(c => c == "recent.list"));
		Assert.Single(recent.Rows);
	}

	[Fact]
	public async Task Open_PassesTheSessionToTheShell()
	{
		Serve(Row("a", "Alpha"));
		using var recent = Create();
		await recent.AttachAsync(_client);

		recent.OpenCommand.Execute(recent.Rows[0]);

		Assert.Equal(["a:Alpha"], _actions.Opened);
	}

	[Fact]
	public async Task ToggleFavorite_SetsTheFlagAndReloadsTheList()
	{
		Serve(Row("a", "A"));
		using var recent = Create();
		await recent.AttachAsync(_client);
		var calls = new List<(string Id, bool Value)>();
		_actions.SetFavorite = (id, value) =>
		{
			calls.Add((id, value));
			Serve(Row("a", "A", pinned: true, favorite: true));   // the daemon now pins it
			return value;
		};

		await recent.ToggleFavoriteCommand.ExecuteAsync(recent.Rows[0]);

		Assert.Equal([("a", true)], calls);
		Assert.True(recent.Rows[0].IsFavorite);
		Assert.False(recent.Rows[0].CanHide);
	}

	[Fact]
	public async Task ToggleFavorite_WhenTheCallFails_KeepsTheRow()
	{
		Serve(Row("a", "A"));
		_actions.SetFavorite = (_, _) => null;
		using var recent = Create();
		await recent.AttachAsync(_client);

		await recent.ToggleFavoriteCommand.ExecuteAsync(recent.Rows[0]);

		Assert.False(recent.Rows[0].IsFavorite);
		Assert.Equal(1, _client.Calls.Count(c => c == "recent.list"));
	}

	[Fact]
	public async Task SessionChanged_ThatChangesNothingTheListDependsOn_UpdatesTheRowInPlaceWithoutReloading()
	{
		Serve(Row("a", "A"), Row("b", "B"));
		using var recent = Create();
		await recent.AttachAsync(_client);
		var row = recent.Rows[0];
		var same = row.State with { HasDraft = true, ProfileName = "work" };

		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(same));

		Assert.Same(row, recent.Rows[0]);
		Assert.True(row.HasDraft);
		Assert.Equal("work", row.ProfileName);
		Assert.Equal(1, _client.Calls.Count(c => c == "recent.list"));
	}

	[Fact]
	public async Task SessionChanged_ForAnUnlistedSession_ReloadsTheList()
	{
		Serve(Row("a", "A"));
		using var recent = Create();
		await recent.AttachAsync(_client);
		Serve(Row("n", "New"), Row("a", "A"));

		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(State("n", running: true)));
		await recent.RefreshTask;

		Assert.Equal(["n", "a"], recent.Rows.Select(r => r.Id));
	}

	[Fact]
	public async Task SessionChanged_FavouriteFlip_ReloadsBecauseTheRowIsPinnedNow()
	{
		Serve(Row("a", "A"), Row("b", "B"));
		using var recent = Create();
		await recent.AttachAsync(_client);
		Serve(Row("b", "B", pinned: true, favorite: true), Row("a", "A"));

		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(State("b", favorite: true)));
		await recent.RefreshTask;

		Assert.Equal(["b", "a"], recent.Rows.Select(r => r.Id));
		Assert.True(recent.Rows[0].IsFavorite);
	}

	[Fact]
	public async Task Reload_KeepsTheRowObjectsOfSessionsThatStay()
	{
		Serve(Row("a", "A"), Row("b", "B"));
		using var recent = Create();
		await recent.AttachAsync(_client);
		var b = recent.Rows[1];
		Serve(Row("b", "B renamed"), Row("c", "C"));

		await recent.RefreshAsync();

		Assert.Equal(["b", "c"], recent.Rows.Select(r => r.Id));
		Assert.Same(b, recent.Rows[0]);
		Assert.Equal("B renamed", b.Name);
	}

	[Fact]
	public async Task Resync_ReloadsTheList()
	{
		Serve(Row("a", "A"));
		using var recent = Create();
		await recent.AttachAsync(_client);
		Serve(Row("z", "Z"));

		_client.RaiseResync();
		await recent.RefreshTask;

		Assert.Equal(["z"], recent.Rows.Select(r => r.Id));
	}

	[Fact]
	public async Task DaemonFailure_IsShownAndKeepsTheOldRows()
	{
		Serve(Row("a", "A"));
		using var recent = Create();
		await recent.AttachAsync(_client);
		_client.RecentListHandler = () => throw new InvalidOperationException("boom");

		await recent.RefreshAsync();

		Assert.Equal("boom", recent.Error);
		Assert.Single(recent.Rows);
	}

	[Fact]
	public async Task NotificationsOfOtherKinds_AreIgnored()
	{
		Serve(Row("a", "A"));
		using var recent = Create();
		await recent.AttachAsync(_client);

		_client.RaiseNotification(RemoteProtocol.Events.TagsChanged, new TagsChangedPayload(null));

		Assert.Equal(1, _client.Calls.Count(c => c == "recent.list"));
	}
}
