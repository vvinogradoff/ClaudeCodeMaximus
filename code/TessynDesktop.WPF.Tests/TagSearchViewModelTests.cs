using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The Tags tab (FR.1.15, FR.24.11-24.14): search text, chips, OR selection, grouped sessions, refresh on tags.changed (W3).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagSearchViewModelTests
{
	private sealed class Actions : ISessionListActions
	{
		public List<string> Opened { get; } = [];

		public void Open(string nodeId, string name) => Opened.Add(nodeId);

		public Task<bool?> SetFavoriteAsync(string nodeId, bool value) => Task.FromResult<bool?>(value);
	}

	private readonly FakeTessynClient _client = new();
	private readonly Actions _actions = new();

	private TagSearchViewModel Create() => new(_actions, new ImmediateUiDispatcher(), NullLogger<TagSearchViewModel>.Instance, TimeSpan.Zero);

	private static SessionStateDto State(string id, bool favorite = false) => new(id, false, false, null, favorite, DateTimeOffset.UtcNow, null, false, null);

	private static TagChipDto Chip(string key, int count, bool selected = false) => new(key, key.ToUpperInvariant(), "https://x/" + key, SessionTagKind.LinearIssue, count, selected);

	/// <summary>A daemon: tags a (2 sessions) and b (1); the sessions of the selected tags are listed.</summary>
	private void ServeTags()
	{
		_client.TagsSearchHandler = (text, selected) =>
		{
			var keys = selected.Select(t => t.Key).ToHashSet();
			var chips = new List<TagChipDto>();
			foreach (var key in new[] { "a", "b" }.OrderByDescending(keys.Contains))
			{
				if (text.Length == 0 || key.Contains(text, StringComparison.OrdinalIgnoreCase))
					chips.Add(Chip(key, key == "a" ? 2 : 1, keys.Contains(key)));
			}

			foreach (var gone in selected.Where(t => chips.All(c => c.Key != t.Key)))
				chips.Insert(0, new TagChipDto(gone.Key, gone.Label, gone.Url, gone.Kind, 0, true));

			var rows = new List<SessionRowDto>();
			if (keys.Contains("a"))
				rows.AddRange([new SessionRowDto("s1", "One", State("s1")), new SessionRowDto("s2", "Two", State("s2", favorite: true))]);
			if (keys.Contains("b") && rows.All(r => r.Id != "s3"))
				rows.Add(new SessionRowDto("s3", "Three", State("s3")));

			var groups = rows.Count == 0 ? [] : new[] { new SessionGroupDto("d1", "Repo", rows) };
			return new TagSearchResult(chips.Count == 0 ? TagEmptyReason.NoMatchingTags : null, chips,
				keys.Count == 0 ? TagSessionsEmptyReason.SelectTags : rows.Count == 0 ? TagSessionsEmptyReason.NoSessions : null, groups);
		};
	}

	[Fact]
	public async Task Attach_RunsTheFirstSearch_ChipsAppearAndNoSessionsAreListed()
	{
		ServeTags();
		using var tags = Create();

		await tags.AttachAsync(_client);

		Assert.Equal(["a", "b"], tags.Chips.Select(c => c.Key));
		Assert.Equal("A (2)", tags.Chips[0].Text);
		Assert.Empty(tags.Groups);
		Assert.Equal(Constants.SessionLists.TagsSelectTagsText, tags.EmptySessionsText);
		Assert.Contains("tags.search::", _client.Calls);
	}

	[Fact]
	public async Task SelectingAChip_SendsItBackAndListsItsSessionsByDirectory()
	{
		ServeTags();
		using var tags = Create();
		await tags.AttachAsync(_client);

		await tags.ToggleChipCommand.ExecuteAsync(tags.Chips[0]);

		Assert.Contains("tags.search::a", _client.Calls);
		Assert.True(tags.Chips[0].Selected);
		Assert.Equal("Repo", Assert.Single(tags.Groups).DirectoryLabel);
		Assert.Equal(["s1", "s2"], tags.Groups[0].Sessions.Select(s => s.Id));
		Assert.True(tags.Groups[0].Sessions[1].IsFavorite);
		Assert.Null(tags.EmptySessionsText);
	}

	[Fact]
	public async Task SelectedChipsAreAnOrFilter_SessionsOfEitherTagAreListed()
	{
		ServeTags();
		using var tags = Create();
		await tags.AttachAsync(_client);

		await tags.ToggleChipCommand.ExecuteAsync(tags.Chips.Single(c => c.Key == "b"));
		await tags.ToggleChipCommand.ExecuteAsync(tags.Chips.Single(c => c.Key == "a"));

		Assert.Equal(["a", "b"], tags.SelectedTags.Select(t => t.Key).OrderBy(k => k));
		Assert.Contains("tags.search::b,a", _client.Calls);
		Assert.Equal(["s1", "s2", "s3"], tags.Groups[0].Sessions.Select(s => s.Id));
		Assert.All(tags.Chips, c => Assert.True(c.Selected));
	}

	[Fact]
	public async Task TogglingASelectedChip_DropsItFromTheFilter()
	{
		ServeTags();
		using var tags = Create();
		await tags.AttachAsync(_client);
		await tags.ToggleChipCommand.ExecuteAsync(tags.Chips[0]);

		await tags.ToggleChipCommand.ExecuteAsync(tags.Chips.Single(c => c.Key == "a"));

		Assert.Empty(tags.SelectedTags);
		Assert.Empty(tags.Groups);
		Assert.Equal(Constants.SessionLists.TagsSelectTagsText, tags.EmptySessionsText);
	}

	[Fact]
	public async Task TypingText_SearchesTheTagsAndKeepsTheSelection()
	{
		ServeTags();
		using var tags = Create();
		await tags.AttachAsync(_client);
		await tags.ToggleChipCommand.ExecuteAsync(tags.Chips[0]);

		tags.SearchText = "b";
		await tags.SearchTask;

		Assert.Contains("tags.search:b:a", _client.Calls);
		Assert.Equal(["a", "b"], tags.Chips.Select(c => c.Key));   // the selected one stays (here as a stub); matches follow
	}

	[Fact]
	public async Task NoMatchingTag_ShowsTheReason()
	{
		ServeTags();
		using var tags = Create();
		await tags.AttachAsync(_client);

		tags.SearchText = "zzz";
		await tags.SearchTask;

		Assert.Empty(tags.Chips);
		Assert.Equal(Constants.SessionLists.TagsNoMatchText, tags.EmptyChipsText);
	}

	[Fact]
	public async Task AVanishedSelectedTag_StaysAsAStubChipTheUserCanDeselect()
	{
		ServeTags();
		using var tags = Create();
		await tags.AttachAsync(_client);
		await tags.ToggleChipCommand.ExecuteAsync(tags.Chips[0]);
		tags.SearchText = "q";
		await tags.SearchTask;

		var stub = tags.Chips.Single(c => c.Key == "a");
		Assert.True(stub.IsStub);
		Assert.Equal("A", stub.Text);

		await tags.ToggleChipCommand.ExecuteAsync(stub);
		Assert.Empty(tags.SelectedTags);
	}

	[Fact]
	public async Task TagsChanged_SearchesAgainWithTheCurrentSelection()
	{
		ServeTags();
		using var tags = Create();
		await tags.AttachAsync(_client);
		await tags.ToggleChipCommand.ExecuteAsync(tags.Chips[0]);
		var before = _client.Calls.Count(c => c.StartsWith("tags.search", StringComparison.Ordinal));

		_client.RaiseNotification(RemoteProtocol.Events.TagsChanged, new TagsChangedPayload(null));
		await tags.SearchTask;

		Assert.Equal(before + 1, _client.Calls.Count(c => c.StartsWith("tags.search", StringComparison.Ordinal)));
		Assert.Equal("tags.search::a", _client.Calls.Last(c => c.StartsWith("tags.search", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task SessionChanged_UpdatesTheListedRowInPlace()
	{
		ServeTags();
		using var tags = Create();
		await tags.AttachAsync(_client);
		await tags.ToggleChipCommand.ExecuteAsync(tags.Chips[0]);
		var row = tags.Groups[0].Sessions[0];

		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(State("s1", favorite: true)));

		Assert.True(row.IsFavorite);
	}

	[Fact]
	public async Task OpeningARow_PassesTheSessionToTheShell()
	{
		ServeTags();
		using var tags = Create();
		await tags.AttachAsync(_client);
		await tags.ToggleChipCommand.ExecuteAsync(tags.Chips[0]);

		tags.OpenCommand.Execute(tags.Groups[0].Sessions[1]);

		Assert.Equal(["s2"], _actions.Opened);
	}

	[Fact]
	public async Task ToggleFavoriteOnARow_FlipsItsFlag()
	{
		ServeTags();
		using var tags = Create();
		await tags.AttachAsync(_client);
		await tags.ToggleChipCommand.ExecuteAsync(tags.Chips[0]);
		var row = tags.Groups[0].Sessions[0];

		await tags.ToggleFavoriteCommand.ExecuteAsync(row);

		Assert.True(row.IsFavorite);
	}

	[Fact]
	public async Task Clear_DropsSelectionAndText_WithOneSearch()
	{
		ServeTags();
		using var tags = Create();
		await tags.AttachAsync(_client);
		await tags.ToggleChipCommand.ExecuteAsync(tags.Chips[0]);
		tags.SearchText = "a";
		await tags.SearchTask;
		var before = _client.Calls.Count(c => c.StartsWith("tags.search", StringComparison.Ordinal));

		await tags.ClearCommand.ExecuteAsync(null);

		Assert.Equal(before + 1, _client.Calls.Count(c => c.StartsWith("tags.search", StringComparison.Ordinal)));
		Assert.Equal(string.Empty, tags.SearchText);
		Assert.Empty(tags.SelectedTags);
		Assert.Empty(tags.Groups);
	}

	[Fact]
	public async Task ANewerSearchSupersedesAnOlderOne_OnlyTheLastResultIsShown()
	{
		_client.TagsSearchHandler = (text, _) => new TagSearchResult(null, [Chip(text.Length == 0 ? "empty" : text, 1)], TagSessionsEmptyReason.SelectTags, []);
		using var tags = Create();
		await tags.AttachAsync(_client);

		tags.SearchText = "x";
		tags.SearchText = "xy";
		await tags.SearchTask;

		Assert.Equal(["xy"], tags.Chips.Select(c => c.Key));
	}

	[Fact]
	public async Task DaemonFailure_IsShown()
	{
		_client.TagsSearchHandler = (_, _) => throw new InvalidOperationException("no index");
		using var tags = Create();

		await tags.AttachAsync(_client);

		Assert.Equal("no index", tags.Error);
	}
}
