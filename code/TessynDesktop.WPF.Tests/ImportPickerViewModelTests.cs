using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>W7 (FR.13): the import picker over the J4 methods: the directory list, progressive titles, Enter search, Global, selection rules, paging and the import result.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ImportPickerViewModelTests
{
	private static readonly DateTimeOffset At = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

	private readonly FakeTessynClient _client = new();
	private readonly ImportTargetViewModel _repo = new("d1", "Repo", 0, true, "d1");
	private readonly ImportTargetViewModel _group = new("g1", "Team", 1, false, "d1");
	private readonly ImportTargetViewModel _other = new("d2", "Other", 0, true, "d2");

	private ImportPickerViewModel Create(int pageSize = 100, int maxPolls = 10) => new(_client, NullLogger.Instance, TimeSpan.Zero, maxPolls, pageSize);

	private static ImportCandidateDto Candidate(string id, string? title = null, string prompt = "hello", bool empty = false, bool imported = false) =>
		new(id, title, prompt, At, empty ? 0 : 4, empty, imported, "proj");

	private static async Task WaitAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition() && DateTime.UtcNow < deadline)
			await Task.Delay(10);

		Assert.True(condition(), "The condition was not met in time.");
	}

	private async Task<ImportPickerViewModel> OpenAsync(string? target = null, int pageSize = 100)
	{
		var picker = Create(pageSize);
		await picker.InitializeAsync([_repo, _group, _other], target);
		return picker;
	}

	[Fact]
	public async Task Opening_ListsTheSessionsOfTheDirectoryOfTheTarget()
	{
		_client.ImportListHandler = (dir, _, _, _, _) => new ImportListResult([Candidate(dir + "-a", "A")], 1);

		using var picker = await OpenAsync("g1");

		Assert.Equal("d1", picker.SelectedSource?.Id);
		Assert.Equal("g1", picker.SelectedTarget?.Id);   // opened on the group: it stays the target
		Assert.Equal(["d1-a"], picker.Items.Select(i => i.ExternalId));
		Assert.Equal(["d1", "d2"], picker.Sources.Select(s => s.Id));   // groups are targets only
		Assert.Contains("import.list:d1:False:", _client.Calls);
	}

	[Fact]
	public async Task ChangingTheSource_RelistsAndMovesTheTarget_UnlessTheUserChoseOne()
	{
		_client.ImportListHandler = (dir, _, _, _, _) => new ImportListResult([Candidate(dir + "-a", "A")], 1);
		using var picker = await OpenAsync("d1");

		picker.SelectedSource = _other;
		Assert.Equal("d2", picker.SelectedTarget?.Id);
		Assert.Equal(["d2-a"], picker.Items.Select(i => i.ExternalId));

		picker.SelectedTarget = _group;   // the user picks a target on purpose
		picker.SelectedSource = _repo;
		Assert.Equal("g1", picker.SelectedTarget?.Id);
	}

	[Fact]
	public async Task EmptyAndAlreadyImportedSessions_AreGreyedAndCannotBeTicked()
	{
		_client.ImportListHandler = (_, _, _, _, _) => new ImportListResult([Candidate("ok", "Ok"), Candidate("empty", empty: true), Candidate("done", "Done", imported: true)], 3);
		using var picker = await OpenAsync();

		var ok = picker.Items[0];
		var empty = picker.Items[1];
		var done = picker.Items[2];
		Assert.True(ok.IsSelectable);
		Assert.False(empty.IsSelectable || done.IsSelectable);
		Assert.True(empty.IsGreyed && done.IsGreyed);
		Assert.Equal(Constants.Import.EmptySession, empty.BadgeText);
		Assert.Equal(Constants.Import.AlreadyImported, done.BadgeText);

		picker.SelectAllCommand.Execute(null);

		Assert.Equal(1, picker.SelectedCount);   // only the importable one
		Assert.False(empty.IsSelected || done.IsSelected);
	}

	[Fact]
	public async Task Titles_ArriveProgressively_WhileSomeArePending_AndOnlyForRowsWithoutOne()
	{
		_client.ImportListHandler = (_, _, _, _, _) => new ImportListResult([Candidate("a", "Has title"), Candidate("b", null, "first prompt of b"), Candidate("c", null, "first prompt of c")], 3);
		var round = 0;
		_client.ImportTitlesHandler = ids => ++round switch
		{
			1 => new ImportTitlesResult([new ImportTitleDto("b", "Title B")], 1),
			_ => new ImportTitlesResult([new ImportTitleDto("c", "Title C")], 0),
		};

		using var picker = await OpenAsync();
		await WaitAsync(() => picker.Items.All(i => i.HasTitle) && !picker.IsGeneratingTitles);

		Assert.Equal(["Has title", "Title B", "Title C"], picker.Items.Select(i => i.DisplayTitle));
		Assert.Equal(2, round);
		Assert.Contains("import.titles:b,c", _client.Calls);   // the row that had a title was not asked for
		Assert.Contains("import.titles:c", _client.Calls);   // the second round asks only for what is missing
	}

	[Fact]
	public async Task ARowWithoutATitle_ShowsItsFirstPrompt_UntilOneArrives()
	{
		_client.ImportListHandler = (_, _, _, _, _) => new ImportListResult([Candidate("b", null, "Fix the build\nand more")], 1);
		_client.ImportTitlesHandler = _ => new ImportTitlesResult([], 1);   // never gets one
		using var picker = await OpenAsync();

		await WaitAsync(() => !picker.IsGeneratingTitles);   // gives up after the bounded number of rounds

		Assert.Equal("Fix the build", picker.Items[0].DisplayTitle);
		Assert.True(_client.Calls.Count(c => c.StartsWith("import.titles", StringComparison.Ordinal)) <= 10);
	}

	[Fact]
	public async Task ASlowTitleOfAnOldList_DoesNotTouchTheNewList()
	{
		var gate = new TaskCompletionSource();
		_client.ImportListHandler = (dir, _, _, _, _) => new ImportListResult([Candidate(dir + "-a", null, "p")], 1);
		_client.ImportTitlesHandler = ids =>
		{
			gate.Task.Wait(TimeSpan.FromSeconds(2));
			return new ImportTitlesResult([new ImportTitleDto(ids[0], "late " + ids[0])], 0);
		};
		using var picker = await OpenAsync();   // d1's title request is held

		picker.SelectedSource = _other;
		gate.SetResult();
		await Task.Delay(100);

		Assert.Equal(["d2-a"], picker.Items.Select(i => i.ExternalId));
		Assert.DoesNotContain("late d1-a", picker.Items.Select(i => i.DisplayTitle));
	}

	[Fact]
	public async Task Search_FiltersTheDirectoryList_WithTheQuery()
	{
		_client.ImportListHandler = (_, _, query, _, _) => new ImportListResult(query == "cron" ? [Candidate("hit", "Cron job")] : [Candidate("hit", "Cron job"), Candidate("miss", "Other")], query == "cron" ? 1 : 2);
		using var picker = await OpenAsync();
		Assert.Equal(2, picker.Items.Count);

		picker.SearchText = " cron ";
		await picker.SearchCommand.ExecuteAsync(null);

		Assert.Equal(["hit"], picker.Items.Select(i => i.ExternalId));
		Assert.Contains("import.list:d1:False:cron", _client.Calls);
	}

	[Fact]
	public async Task Global_SearchesEveryProject_OnlyWhenAQueryIsEntered_AndFlagsTheImportedHits()
	{
		_client.ImportListHandler = (_, _, _, _, _) => new ImportListResult([Candidate("local", "Local")], 1);
		_client.SearchGlobalHandler = (query, _, _, _) => new SearchGlobalResult(
			[new SearchHitDto("x1", "From elsewhere", "C--Projects-Other", "…the matching message…", 3.5, null), new SearchHitDto("x2", "Known", "C--Projects-Repo", "snippet", 2.0, "node-9")], 2);
		using var picker = await OpenAsync();

		picker.IsGlobal = true;
		Assert.Empty(picker.Items);   // nothing is listed before a query: global without a query would be the whole index
		Assert.Equal(Constants.Import.SearchHint, picker.StatusText);
		Assert.False(picker.ShowSourcePicker);

		await picker.SearchCommand.ExecuteAsync(null);
		Assert.Empty(picker.Items);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("search.global", StringComparison.Ordinal));

		picker.SearchText = "matching";
		await picker.SearchCommand.ExecuteAsync(null);

		Assert.Equal(["x1", "x2"], picker.Items.Select(i => i.ExternalId));
		Assert.Contains("search.global:matching", _client.Calls);
		Assert.True(picker.Items[0].IsSelectable);
		Assert.False(picker.Items[1].IsSelectable);   // already in the tree
		Assert.Equal("node-9", picker.Items[1].ImportedNodeId);
		Assert.Equal("C--Projects-Other", picker.Items[0].ProjectSlug);

		picker.IsGlobal = false;   // back to the directory
		await WaitAsync(() => picker.Items.Count == 1 && picker.Items[0].ExternalId == "local");
	}

	[Fact]
	public async Task LoadMore_AppendsTheNextPage_WhileTheTotalIsLarger()
	{
		_client.ImportListHandler = (_, _, _, limit, offset) =>
			new ImportListResult([.. Enumerable.Range(offset ?? 0, Math.Min(limit ?? 2, 5 - (offset ?? 0))).Select(i => Candidate("s" + i, "T" + i))], 5);
		using var picker = await OpenAsync(pageSize: 2);
		Assert.Equal(2, picker.Items.Count);
		Assert.True(picker.CanLoadMoreItems);

		await picker.LoadMoreCommand.ExecuteAsync(null);
		await picker.LoadMoreCommand.ExecuteAsync(null);

		Assert.Equal(["s0", "s1", "s2", "s3", "s4"], picker.Items.Select(i => i.ExternalId));
		Assert.False(picker.CanLoadMoreItems);
		Assert.False(picker.LoadMoreCommand.CanExecute(null));
	}

	[Fact]
	public async Task Import_SendsTheTickedSessionsToTheTarget_MarksThemImportedAndClosesWhenAllWent()
	{
		_client.ImportListHandler = (_, _, _, _, _) => new ImportListResult([Candidate("a", "A"), Candidate("b", "B"), Candidate("c", "C")], 3);
		using var picker = await OpenAsync("g1");
		IReadOnlyList<string>? imported = null;
		var closed = false;
		picker.Imported += (_, ids) => imported = ids;
		picker.CloseRequested += (_, _) => closed = true;
		Assert.False(picker.ImportCommand.CanExecute(null));   // nothing ticked

		picker.Items[0].IsSelected = true;
		picker.Items[2].IsSelected = true;
		Assert.True(picker.ImportCommand.CanExecute(null));
		await picker.ImportCommand.ExecuteAsync(null);

		Assert.Contains("import.run:g1:a,c", _client.Calls);
		Assert.Equal(["node-a", "node-c"], imported);
		Assert.True(closed);
		Assert.True(picker.Items[0].AlreadyImported && picker.Items[2].AlreadyImported);
		Assert.False(picker.Items[0].IsSelected);
		Assert.Equal(0, picker.SelectedCount);
	}

	[Fact]
	public async Task Import_WithAFailure_StaysOpenAndSaysWhy()
	{
		_client.ImportListHandler = (_, _, _, _, _) => new ImportListResult([Candidate("a", "A"), Candidate("b", "B")], 2);
		_client.ImportRunHandler = (_, items) => new ImportRunResult([new ImportedSessionDto("node-a", "a")], [new ImportFailureDto("b", "The session has no messages")]);
		using var picker = await OpenAsync();
		var closed = false;
		picker.CloseRequested += (_, _) => closed = true;
		picker.SelectAllCommand.Execute(null);

		await picker.ImportCommand.ExecuteAsync(null);

		Assert.False(closed);
		Assert.Contains("Imported 1 session", picker.StatusText);
		Assert.Contains("The session has no messages", picker.StatusText);
		Assert.True(picker.Items[0].AlreadyImported);
		Assert.False(picker.Items[1].AlreadyImported);
		Assert.True(picker.Items[1].IsSelected);   // still ticked, can be tried again
	}

	[Fact]
	public async Task Import_ThatThrows_ShowsTheReasonAndKeepsTheTicks()
	{
		_client.ImportListHandler = (_, _, _, _, _) => new ImportListResult([Candidate("a", "A")], 1);
		_client.ImportRunHandler = (_, _) => throw new InvalidOperationException("unknown target");
		using var picker = await OpenAsync();
		picker.SelectAllCommand.Execute(null);

		await picker.ImportCommand.ExecuteAsync(null);

		Assert.Contains("unknown target", picker.StatusText);
		Assert.Equal(1, picker.SelectedCount);
		Assert.False(picker.IsBusy);
	}

	[Fact]
	public async Task AFailedList_ShowsTheReason()
	{
		_client.ImportListHandler = (_, _, _, _, _) => throw new InvalidOperationException("no such directory");

		using var picker = await OpenAsync();

		Assert.Contains("no such directory", picker.StatusText);
		Assert.False(picker.IsBusy);
		Assert.Empty(picker.Items);
	}

	[Fact]
	public async Task WithoutAnyDirectory_ThePickerIsEmptyAndCannotImport()
	{
		using var picker = Create();

		await picker.InitializeAsync([], null);

		Assert.Null(picker.SelectedTarget);
		Assert.Empty(picker.Items);
		Assert.False(picker.ImportCommand.CanExecute(null));
	}

	[Fact]
	public async Task TheTreeTargets_ListDirectoriesAndGroupsInOrder_WithTheOwningDirectory()
	{
		var state = new SessionStateDto("s", false, false, null, false, null, null, false, null);
		var sub = new TreeGroupDto("n", "Sub", false, [], []);
		var team = new TreeGroupDto("g", "Team", false, [sub], []);
		_client.Directories =
		[
			new TreeDirectoryDto("d", "Repo", false, [team], [new TreeSessionDto("s", "S", 0, null, state)]),
			new TreeDirectoryDto("e", "Second", false, [], []),
		];
		var tree = new TreeViewModel(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();

		var targets = tree.ImportTargets();

		Assert.Equal([("d", 0, true, "d"), ("g", 1, false, "d"), ("n", 2, false, "d"), ("e", 0, true, "e")], targets.Select(t => (t.Id, t.Depth, t.IsDirectory, t.DirectoryId)));
		Assert.Equal("Repo", targets[0].DisplayText);
		Assert.EndsWith("↳ Team", targets[1].DisplayText);
	}
}
