using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>W1b drafts: the unsent text goes to the daemon through <c>draft.set</c> after a quiet moment, comes back when a session opens, and is merged (never lost) with what another client wrote.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ComposerDraftTests : IDisposable
{
	private const string A = "a";
	private const string B = "b";

	private readonly FakeTessynClient _client = new();
	private readonly FakeTranscriptSurface _surface = new();
	private readonly List<SessionPaneViewModel> _panes = [];

	public ComposerDraftTests()
	{
		_client.Sessions[A] = FakeSession.WithEntries(2, "Session A");
		_client.Sessions[B] = FakeSession.WithEntries(2, "Session B");
	}

	public void Dispose()
	{
		foreach (var pane in _panes)
			pane.Dispose();
	}

	private SessionPaneViewModel CreatePane(TimeSpan? saveDelay = null)
	{
		var pane = new SessionPaneViewModel(_client, _surface, new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance)
		{
			LiveFlushInterval = TimeSpan.Zero,
		};
		pane.DraftSync.Delay = saveDelay ?? TimeSpan.Zero;
		_panes.Add(pane);
		return pane;
	}

	private static async Task WaitForAsync(Func<bool> condition, string what)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!condition() && DateTime.UtcNow < deadline)
			await Task.Delay(10);

		Assert.True(condition(), "Not reached in time: " + what);
	}

	private int Count(string prefix) => _client.Calls.Count(c => c.StartsWith(prefix, StringComparison.Ordinal));

	// ---- saving ----

	[Fact]
	public async Task TypedText_IsWrittenAsTheSessionsDraft()
	{
		var pane = CreatePane();
		await pane.OpenAsync(A);

		pane.InputText = "half a thought";

		await WaitForAsync(() => _client.DraftTexts.GetValueOrDefault(A) == "half a thought", "draft.set");
	}

	[Fact]
	public async Task ABurstOfTyping_IsWrittenOnce_AfterTheQuietMoment()
	{
		var pane = CreatePane(TimeSpan.FromMilliseconds(200));
		await pane.OpenAsync(A);
		await WaitForAsync(() => Count("draft.get:a") == 1, "draft.get");

		foreach (var text in new[] { "h", "he", "hel", "hell", "hello" })
		{
			pane.InputText = text;
			await Task.Delay(20);
		}

		Assert.Equal(0, Count("draft.set:a"));   // still typing
		await WaitForAsync(() => _client.DraftTexts.GetValueOrDefault(A) == "hello", "draft.set");
		Assert.Equal(1, Count("draft.set:a"));
	}

	[Fact]
	public async Task SwitchingToAnotherSession_WritesTheLeftDraftAtOnce_AndItComesBack()
	{
		var pane = CreatePane(TimeSpan.FromMinutes(5));   // the debounce alone would never fire
		await pane.OpenAsync(A);
		pane.InputText = "unsent in a";

		await pane.OpenAsync(B);

		await WaitForAsync(() => _client.DraftTexts.GetValueOrDefault(A) == "unsent in a", "draft of a written at the switch");
		Assert.Equal(string.Empty, pane.InputText);
		await pane.OpenAsync(A);
		Assert.Equal("unsent in a", pane.InputText);
	}

	[Fact]
	public async Task SendingClearsTheInput_AndDeletesTheDraft()
	{
		var pane = CreatePane();
		await pane.OpenAsync(A);
		pane.InputText = "go";
		await WaitForAsync(() => _client.DraftTexts.ContainsKey(A), "draft saved");

		await pane.SendCommand.ExecuteAsync(null);

		await WaitForAsync(() => !_client.DraftTexts.ContainsKey(A), "draft deleted by the empty write");
	}

	[Fact]
	public async Task FlushAll_WritesWhatIsStillWaiting()
	{
		var pane = CreatePane(TimeSpan.FromMinutes(5));
		await pane.OpenAsync(A);
		pane.InputText = "typed just before exit";

		await pane.DraftSync.FlushAllAsync();

		Assert.Equal("typed just before exit", _client.DraftTexts[A]);
	}

	[Fact]
	public async Task EmptyingTheBox_RemovesTheDraft_AndAnEmptyBoxOnAnEmptySessionWritesNothing()
	{
		var pane = CreatePane();
		await pane.OpenAsync(A);
		await WaitForAsync(() => Count("draft.get:a") == 1, "draft.get");
		pane.InputText = "x";
		await WaitForAsync(() => Count("draft.set:a") == 1, "first write");

		pane.InputText = string.Empty;
		await WaitForAsync(() => Count("draft.set:a") == 2, "delete");
		Assert.False(_client.DraftTexts.ContainsKey(A));

		pane.InputText = "y";
		pane.InputText = string.Empty;
		await Task.Delay(100);
		Assert.False(_client.DraftTexts.ContainsKey(A));
	}

	// ---- reading ----

	[Fact]
	public async Task OpeningASession_ShowsTheDaemonsDraft_WithoutWritingItBack()
	{
		_client.DraftTexts[A] = "written on the phone";
		var pane = CreatePane();

		await pane.OpenAsync(A);

		await WaitForAsync(() => pane.InputText == "written on the phone", "draft shown");
		await Task.Delay(100);
		Assert.Equal(0, Count("draft.set:a"));
	}

	[Fact]
	public async Task TextTypedBeforeTheDraftArrives_IsKept_AndMergedWithIt()
	{
		_client.DraftTexts[A] = "remote";
		var pane = CreatePane(TimeSpan.FromMinutes(5));
		var readGate = new TaskCompletionSource();
		_client.DraftGetGate = readGate;
		await pane.OpenAsync(A);
		pane.InputText = "local";   // typed while the draft is still being read

		readGate.SetResult();

		await WaitForAsync(() => pane.InputText == "remote" + Environment.NewLine + "local", "merged text");
	}

	// ---- another writer ----

	[Fact]
	public async Task AChangeByAnotherClient_ReplacesTheText_WhenNothingWasTypedHere()
	{
		var pane = CreatePane();
		await pane.OpenAsync(A);
		await WaitForAsync(() => Count("draft.get:a") == 1, "draft.get");

		_client.DraftTexts[A] = "from the desktop composer";
		_client.RaiseNotification(RemoteProtocol.Composer.DraftChangedEvent, new DraftChangedPayload(A, "other-revision"));

		await WaitForAsync(() => pane.InputText == "from the desktop composer", "replaced");
		Assert.Equal(0, Count("draft.set:a"));   // no echo back
	}

	[Fact]
	public async Task AChangeByAnotherClient_IsMergedWithText_TypedHere()
	{
		var pane = CreatePane(TimeSpan.FromMinutes(5));
		await pane.OpenAsync(A);
		await WaitForAsync(() => Count("draft.get:a") == 1, "draft.get");
		pane.InputText = "mine";

		_client.DraftTexts[A] = "theirs";
		_client.RaiseNotification(RemoteProtocol.Composer.DraftChangedEvent, new DraftChangedPayload(A, "other-revision"));

		await WaitForAsync(() => pane.InputText == "theirs" + Environment.NewLine + "mine", "merged");
	}

	[Fact]
	public async Task OurOwnWritesEcho_DoesNotCauseAReadBack()
	{
		var pane = CreatePane();
		await pane.OpenAsync(A);
		pane.InputText = "echo me";
		await WaitForAsync(() => _client.DraftTexts.GetValueOrDefault(A) == "echo me", "saved");
		await WaitForAsync(() => pane.DraftSync.IsKnownRevision(A, FakeTessynClient.RevisionOf("echo me")), "revision recorded");
		var reads = Count("draft.get:a");

		_client.RaiseNotification(RemoteProtocol.Composer.DraftChangedEvent, new DraftChangedPayload(A, FakeTessynClient.RevisionOf("echo me")));
		await Task.Delay(100);

		Assert.Equal(reads, Count("draft.get:a"));
	}

	[Fact]
	public async Task ADraftChangeOfAnotherSession_IsReadWhenThatSessionOpens()
	{
		var pane = CreatePane();
		await pane.OpenAsync(A);
		await pane.OpenAsync(B);
		_client.DraftTexts[A] = "new text for a";
		_client.RaiseNotification(RemoteProtocol.Composer.DraftChangedEvent, new DraftChangedPayload(A, "other-revision"));

		await pane.OpenAsync(A);

		await WaitForAsync(() => pane.InputText == "new text for a", "draft of a");
	}

	[Fact]
	public async Task AConflictingWrite_IsMerged_NotLost_AndTheBoxShowsTheMerge()
	{
		var pane = CreatePane(TimeSpan.FromMinutes(5));
		await pane.OpenAsync(A);
		await WaitForAsync(() => Count("draft.get:a") == 1, "draft.get");
		_client.DraftTexts[A] = "already there";   // another writer, and the event was missed

		pane.InputText = "mine";
		await pane.DraftSync.FlushAllAsync();

		var merged = "already there" + Environment.NewLine + "mine";
		Assert.Equal(merged, _client.DraftTexts[A]);
		await WaitForAsync(() => pane.InputText == merged, "the box shows the merge");
	}

	[Fact]
	public async Task AFailedWrite_IsRetriedByTheNextFlush_AndNeverThrows()
	{
		var pane = CreatePane(TimeSpan.FromMinutes(5));
		await pane.OpenAsync(A);
		_client.DraftSetException = new IOException("daemon busy");
		pane.InputText = "keep me";

		await pane.DraftSync.FlushAllAsync();
		Assert.False(_client.DraftTexts.ContainsKey(A));
		_client.DraftSetException = null;
		await pane.DraftSync.FlushAllAsync();

		Assert.Equal("keep me", _client.DraftTexts[A]);
	}

	[Fact]
	public async Task AfterAResync_TheOpenDraftIsReadAgain()
	{
		var pane = CreatePane();
		await pane.OpenAsync(A);
		await WaitForAsync(() => Count("draft.get:a") == 1, "draft.get");
		_client.DraftTexts[A] = "changed while the stream was down";

		_client.RaiseResync();

		await WaitForAsync(() => pane.InputText == "changed while the stream was down", "draft re-read");
	}

	// ---- the merge rule ----

	[Theory]
	[InlineData("same", "same", "base", "same")]
	[InlineData("typed", "", "base", "typed")]                     // the other writer deleted the draft: ours stays
	[InlineData("typed", "remote", "remote", "typed")]               // the daemon did not change
	[InlineData("base", "remote", "base", "remote")]                 // we did not change
	[InlineData("remote plus more", "remote", "old", "remote plus more")]   // ours already contains theirs
	[InlineData("old", "old and new", "x", "old and new")]           // theirs already contains ours
	public void Merge_KeepsTheTextWithoutDuplicating(string local, string remote, string baseText, string expected) =>
		Assert.Equal(expected, DraftMerge.Merge(local, remote, baseText));

	[Fact]
	public void Merge_OfTwoDifferentEdits_PutsTheDaemonsFirst_AndKeepsBoth()
	{
		var merged = DraftMerge.Merge("local edit", "remote edit", "original");

		Assert.Equal("remote edit" + Environment.NewLine + "local edit", merged);
		Assert.Equal("remote\nlocal edit", DraftMerge.Merge("local edit", "remote\n", "original"));
	}

	[Fact]
	public void Merge_WithoutAKnownBase_TakesTheRemoteWhenNothingWasTyped()
	{
		Assert.Equal("remote", DraftMerge.Merge(string.Empty, "remote", null));
	}

	// ---- the synchronizer on its own ----

	[Fact]
	public async Task Synchronizer_NeverOverwritesADraftItNeverRead()
	{
		_client.DraftTexts["z"] = "existing";
		using var sync = new DraftSynchronizer(_client, NullLogger.Instance, TimeSpan.Zero);
		string? mergedSeen = null;
		sync.Merged += (_, m) => mergedSeen = m.Merged;

		sync.Schedule("z", "brand new");
		await sync.FlushAsync("z");

		Assert.Equal("existing" + Environment.NewLine + "brand new", _client.DraftTexts["z"]);
		Assert.Equal(_client.DraftTexts["z"], mergedSeen);
	}
}
