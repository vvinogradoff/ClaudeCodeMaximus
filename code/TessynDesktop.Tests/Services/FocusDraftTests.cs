using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>FR.23.19 (TC.FD.01-TC.FD.05): the Focus reply drafts in the draft store, the client-side helper and the lifecycle that deletes them.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusDraftTests : IDisposable
{
	private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(40);

	private readonly string _dir = Path.Combine(Path.GetTempPath(), "focus-drafts-" + Guid.NewGuid().ToString("N"));
	private readonly DraftService _store;

	public FocusDraftTests() => _store = new DraftService(Path.Combine(_dir, "drafts"));

	public void Dispose() => Directory.Delete(_dir, recursive: true);

	private static bool WaitUntil(Func<bool> condition)
	{
		var until = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < until)
		{
			if (condition())
				return true;
			Thread.Sleep(10);
		}
		return condition();
	}

	// -- The store -----------------------------------------------------------------------------------------------------------------

	[Fact]
	public void AnItemDraft_RoundTrips_AndSurvivesANewStoreOverTheSameDirectory()
	{
		_store.SaveItemDraft("s.txt", "A1", "my reply\nsecond line");

		var restarted = new DraftService(Path.Combine(_dir, "drafts"));
		Assert.Equal("my reply\nsecond line", restarted.LoadItemDraft("s.txt", "A1"));
		Assert.Null(restarted.LoadItemDraft("s.txt", "A2"));
	}

	[Fact]
	public void ItemDraftsAreKeptPerSessionAndPerItem_AndApartFromTheComposerDraft()
	{
		_store.SaveDraft("s.txt", "composer");
		_store.SaveItemDraft("s.txt", "A1", "one");
		_store.SaveItemDraft("s.txt", "A2", "two");
		_store.SaveItemDraft("other.txt", "A1", "other session");

		Assert.Equal("composer", _store.LoadDraft("s.txt"));
		Assert.Equal("one", _store.LoadItemDraft("s.txt", "A1"));
		Assert.Equal("two", _store.LoadItemDraft("s.txt", "A2"));
		Assert.Equal("other session", _store.LoadItemDraft("other.txt", "A1"));
		Assert.Equal(["A1", "A2"], _store.ListItemDraftIds("s.txt").Order().ToArray());

		_store.DeleteItemDraft("s.txt", "A1");
		Assert.Null(_store.LoadItemDraft("s.txt", "A1"));
		Assert.Equal("composer", _store.LoadDraft("s.txt"));
	}

	[Fact]
	public void ItemIdsThatDifferOnlyInCase_AddressOneDraft()
	{
		_store.SaveItemDraft("s.txt", "A3", "x");

		Assert.Equal("x", _store.LoadItemDraft("s.txt", "a3"));
		_store.SaveItemDraft("s.txt", "a3", "y");
		Assert.Equal("y", _store.LoadItemDraft("s.txt", "A3"));
		Assert.Single(_store.ListItemDraftIds("s.txt"));
		_store.DeleteItemDraft("s.txt", "A3");
		Assert.Null(_store.LoadItemDraft("s.txt", "a3"));
	}

	[Fact]
	public void DeleteItemDrafts_RemovesEveryReplyDraftOfTheSession_AndKeepsTheOthers()
	{
		_store.SaveDraft("s.txt", "composer");
		_store.SaveItemDraft("s.txt", "A1", "one");
		_store.SaveItemDraft("s.txt", "A2", "two");
		_store.SaveItemDraft("other.txt", "A1", "kept");

		_store.DeleteItemDrafts("s.txt");

		Assert.Empty(_store.ListItemDraftIds("s.txt"));
		Assert.Equal("kept", _store.LoadItemDraft("other.txt", "A1"));
		Assert.Equal("composer", _store.LoadDraft("s.txt"));
	}

	[Theory]
	[InlineData("")]
	[InlineData("..\\evil")]
	[InlineData("a/b")]
	[InlineData("a.b")]
	[InlineData("a b")]
	public void AnItemIdThatCannotBePartOfAFileName_IsRefused(string itemId)
	{
		Assert.False(DraftService.IsValidItemId(itemId));
		Assert.Throws<ArgumentException>(() => _store.SaveItemDraft("s.txt", itemId, "x"));
	}

	[Fact]
	public void ItemDraftWrites_RaiseDraftChangedWithTheItemId_AndComposerWritesWithout()
	{
		var raised = new List<(string File, string? Item)>();
		_store.DraftChanged += (_, e) => raised.Add((e.FileName, e.ItemId));

		_store.SaveItemDraft("s.txt", "A1", "x");
		_store.SaveDraft("s.txt", "y");
		_store.DeleteItemDraft("s.txt", "A1");
		_store.DeleteItemDraft("s.txt", "A1"); // missing: nothing raised

		Assert.Equal([("s.txt", (string?)"A1"), ("s.txt", null), ("s.txt", "A1")], raised);
	}

	[Fact]
	public void TryWriteItemDraft_ComparesAndWritesInOneStep()
	{
		_store.SaveItemDraft("s.txt", "A1", "old");

		Assert.False(_store.TryWriteItemDraft("s.txt", "A1", "new", current => current == "other"));
		Assert.Equal("old", _store.LoadItemDraft("s.txt", "A1"));
		Assert.True(_store.TryWriteItemDraft("s.txt", "A1", "new", current => current == "old"));
		Assert.Equal("new", _store.LoadItemDraft("s.txt", "A1"));
		Assert.True(_store.TryWriteItemDraft("s.txt", "A1", "", _ => true));
		Assert.Null(_store.LoadItemDraft("s.txt", "A1"));
	}

	[Fact]
	public async Task ConcurrentWritesOfDifferentItemsAndOfTheSameItem_LoseNothingAndNeverTear()
	{
		var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
		{
			for (var round = 0; round < 25; round++)
			{
				_store.SaveItemDraft("s.txt", "A" + i, "text " + i + " round " + round);
				_store.SaveItemDraft("s.txt", "shared", new string((char)('a' + i % 26), 200));
			}
		})).ToArray();
		await Task.WhenAll(tasks);

		for (var i = 0; i < 20; i++)
			Assert.Equal("text " + i + " round 24", _store.LoadItemDraft("s.txt", "A" + i));

		var shared = _store.LoadItemDraft("s.txt", "shared")!;
		Assert.Equal(200, shared.Length);
		Assert.Single(shared.Distinct());
	}

	[Fact]
	public async Task AReaderPollingTheStore_NeverMakesAWriteFail_NorSeesATornFile()
	{
		// draft.get and the activity scanners read while the composer and the Focus boxes write: a read that holds the file open used to make the write throw (sharing violation).
		var stop = false;
		var failures = new List<Exception>();
		var reader = Task.Run(() =>
		{
			while (!Volatile.Read(ref stop))
			{
				try
				{
					var text = _store.LoadItemDraft("s.txt", "A1");
					if (text != null && text.Length != 4000)
						lock (failures) failures.Add(new InvalidOperationException("torn read of length " + text.Length));
				}
				catch (Exception ex)
				{
					lock (failures) failures.Add(ex);
				}
			}
		});
		var writers = Enumerable.Range(0, 3).Select(w => Task.Run(() =>
		{
			for (var i = 0; i < 400; i++)
			{
				try
				{
					_store.SaveItemDraft("s.txt", "A1", new string((char)('a' + w), 4000));
				}
				catch (Exception ex)
				{
					lock (failures) failures.Add(ex);
				}
			}
		})).ToArray();

		await Task.WhenAll(writers);
		Volatile.Write(ref stop, true);
		await reader;

		Assert.Empty(failures);
	}

	// -- The client-side helper -------------------------------------------------------------------------------------------------------

	/// <summary>Reports every completed write of an item draft through a semaphore, so a test waits for the write itself instead of polling the store or sleeping.</summary>
	private sealed class SignalingDraftService(IDraftService inner) : IDraftService
	{
		private readonly SemaphoreSlim _written = new(0);
		private int _failNextSaves;

		public int Saves;
		public int Deletes;

		/// <summary>The next <paramref name="count"/> saves throw instead of writing.</summary>
		public void FailNextSaves(int count) => Volatile.Write(ref _failNextSaves, count);

		/// <summary>Completes when one more write (save, failed save or delete) has finished; false after the timeout.</summary>
		public Task<bool> NextWriteAsync() => _written.WaitAsync(TimeSpan.FromSeconds(30));

		public string? LoadDraft(string f) => inner.LoadDraft(f);
		public void SaveDraft(string f, string t) => inner.SaveDraft(f, t);
		public void DeleteDraft(string f) => inner.DeleteDraft(f);
		public SessionFileStampModel? GetDraftStamp(string f) => inner.GetDraftStamp(f);
		public DateTimeOffset? GetDraftTimestamp(string f) => inner.GetDraftTimestamp(f);
		public string? LoadItemDraft(string f, string i) => inner.LoadItemDraft(f, i);
		public IReadOnlyList<string> ListItemDraftIds(string f) => inner.ListItemDraftIds(f);

		public void SaveItemDraft(string f, string i, string t)
		{
			if (Interlocked.Decrement(ref _failNextSaves) >= 0)
			{
				_written.Release();
				throw new IOException("simulated failure");
			}

			Interlocked.Exchange(ref _failNextSaves, 0);
			inner.SaveItemDraft(f, i, t);
			Interlocked.Increment(ref Saves);
			_written.Release();
		}

		public void DeleteItemDraft(string f, string i)
		{
			inner.DeleteItemDraft(f, i);
			Interlocked.Increment(ref Deletes);
			_written.Release();
		}
	}

	[Fact]
	public async Task FocusReplyDrafts_WritesAfterThePause_AndAnEmptyTextDeletes()
	{
		var signals = new SignalingDraftService(_store);
		using var drafts = new FocusReplyDrafts(signals, "s.txt", Short);

		drafts.Queue("A1", "typing");
		Assert.True(await signals.NextWriteAsync(), "the debounced write never happened");
		Assert.Equal("typing", _store.LoadItemDraft("s.txt", "A1"));

		drafts.Queue("A1", "   ");
		Assert.True(await signals.NextWriteAsync(), "the debounced delete never happened");
		Assert.Null(_store.LoadItemDraft("s.txt", "A1"));
		Assert.Equal((1, 1), (signals.Saves, signals.Deletes));
	}

	[Fact]
	public async Task FocusReplyDrafts_AFailedWrite_WaitsAgain_AndLandsOnTheNextTry()
	{
		var signals = new SignalingDraftService(_store);
		signals.FailNextSaves(2);
		using var drafts = new FocusReplyDrafts(signals, "s.txt", Short);

		drafts.Queue("A1", "must not be lost");
		for (var attempt = 0; attempt < 3; attempt++)
			Assert.True(await signals.NextWriteAsync(), "attempt " + attempt + " never happened");

		Assert.Equal("must not be lost", _store.LoadItemDraft("s.txt", "A1"));
		Assert.Equal(1, signals.Saves);
	}

	[Fact]
	public async Task FocusReplyDrafts_AFailedWrite_NeverOverwritesANewerText()
	{
		var signals = new SignalingDraftService(_store);
		signals.FailNextSaves(1);
		using var drafts = new FocusReplyDrafts(signals, "s.txt", TimeSpan.FromMinutes(5));
		drafts.Queue("A1", "old text");
		drafts.Flush();                        // fails: back in the queue
		drafts.Queue("A1", "newer text");      // queued meanwhile

		drafts.Flush();

		Assert.True(await signals.NextWriteAsync());
		Assert.True(await signals.NextWriteAsync());
		Assert.Equal("newer text", _store.LoadItemDraft("s.txt", "A1"));
	}

	[Fact]
	public void FocusReplyDrafts_Dispose_FlushesWhatIsStillWaiting_AndANewInstanceRestoresIt()
	{
		var drafts = new FocusReplyDrafts(_store, "s.txt", TimeSpan.FromMinutes(5));
		drafts.Queue("A1", "unsent at close");
		Assert.Null(_store.LoadItemDraft("s.txt", "A1"));

		drafts.Dispose();

		using var afterRestart = new FocusReplyDrafts(new DraftService(Path.Combine(_dir, "drafts")), "s.txt");
		Assert.Equal("unsent at close", afterRestart.Load("A1"));
	}

	[Fact]
	public void FocusReplyDrafts_Discard_DeletesTheStoredDraft_AndForgetsAQueuedText()
	{
		_store.SaveItemDraft("s.txt", "A1", "stored");
		using var drafts = new FocusReplyDrafts(_store, "s.txt", TimeSpan.FromMinutes(5));
		drafts.Queue("A1", "newer");

		drafts.Discard("A1");
		drafts.Flush();

		Assert.Null(_store.LoadItemDraft("s.txt", "A1"));
	}

	[Fact]
	public void FocusReplyDrafts_Prune_DeletesTheDraftsOfItemsThatAreNotOpen_CaseInsensitively()
	{
		_store.SaveItemDraft("s.txt", "A1", "open");
		_store.SaveItemDraft("s.txt", "A2", "closed");
		_store.SaveItemDraft("s.txt", "A3", "gone");
		using var drafts = new FocusReplyDrafts(_store, "s.txt", TimeSpan.FromMinutes(5));

		drafts.Prune(["a1"]);

		Assert.Equal(["A1"], _store.ListItemDraftIds("s.txt").ToArray());
	}

	// -- H1: the exit path flushes every pending draft ---------------------------------------------------------------------------------

	private sealed class CountingFlushable(Action onFlush) : IFlushesDrafts
	{
		public void FlushDrafts() => onFlush();
	}

	[Fact]
	public void DraftFlusher_FlushesEveryItem_EvenWhenOneFails_AndCountsTheFailures()
	{
		var flushed = new List<string>();
		IFlushesDrafts[] items =
		[
			new CountingFlushable(() => flushed.Add("first")),
			new CountingFlushable(() => throw new IOException("disk")),
			new CountingFlushable(() => flushed.Add("third")),
		];

		var failed = DraftFlusher.FlushAll(items);

		Assert.Equal(1, failed);
		Assert.Equal(["first", "third"], flushed);
	}

	[Fact]
	public void TheExitFlush_WritesTheRepliesOfEverySession_WithoutDisposingThem()
	{
		// Two sessions, each with a reply still inside its debounce: what closing the app within 500 ms of the last keystroke leaves behind.
		var first  = new FocusReplyDrafts(_store, "one.txt", TimeSpan.FromMinutes(5));
		var second = new FocusReplyDrafts(_store, "two.txt", TimeSpan.FromMinutes(5));
		first.Queue("A1", "typed in one");
		second.Queue("A1", "typed in two");
		Assert.Null(_store.LoadItemDraft("one.txt", "A1"));

		var failed = DraftFlusher.FlushAll([first, second]);

		Assert.Equal(0, failed);
		Assert.Equal("typed in one", _store.LoadItemDraft("one.txt", "A1"));
		Assert.Equal("typed in two", _store.LoadItemDraft("two.txt", "A1"));

		// Still usable afterwards: a flush is not a dispose.
		first.Queue("A1", "more");
		first.Flush();
		Assert.Equal("more", _store.LoadItemDraft("one.txt", "A1"));
		first.Dispose();
		second.Dispose();
	}

	[Fact]
	public void BoundedLruCache_Snapshot_ListsTheCachedValues_WithoutChangingTheirRecency()
	{
		var cache = new BoundedLruCache<string, string>(2);
		cache.Set("a", "va");
		cache.Set("b", "vb");

		Assert.Equal(["vb", "va"], cache.Snapshot().ToArray());
		cache.Set("c", "vc");
		Assert.Equal(["va"], cache.Trim());   // "a" was still the least recently used
	}

	// -- M2: a discard racing a flush ----------------------------------------------------------------------------------------------------

	private sealed class GatedDraftService(IDraftService inner) : IDraftService
	{
		public ManualResetEventSlim SaveEntered { get; } = new(false);
		public ManualResetEventSlim ReleaseSave { get; } = new(false);

		public string? LoadDraft(string f) => inner.LoadDraft(f);
		public void SaveDraft(string f, string t) => inner.SaveDraft(f, t);
		public void DeleteDraft(string f) => inner.DeleteDraft(f);
		public SessionFileStampModel? GetDraftStamp(string f) => inner.GetDraftStamp(f);
		public DateTimeOffset? GetDraftTimestamp(string f) => inner.GetDraftTimestamp(f);
		public string? LoadItemDraft(string f, string i) => inner.LoadItemDraft(f, i);
		public void DeleteItemDraft(string f, string i) => inner.DeleteItemDraft(f, i);
		public IReadOnlyList<string> ListItemDraftIds(string f) => inner.ListItemDraftIds(f);

		public void SaveItemDraft(string f, string i, string t)
		{
			SaveEntered.Set();
			ReleaseSave.Wait(TimeSpan.FromSeconds(10));
			inner.SaveItemDraft(f, i, t);
		}
	}

	[Fact]
	public void ADiscard_ThatArrivesWhileAFlushIsWriting_NeverLetsTheDeletedDraftComeBack()
	{
		var gated = new GatedDraftService(_store);
		using var drafts = new FocusReplyDrafts(gated, "s.txt", TimeSpan.FromMinutes(5));
		drafts.Queue("A1", "the reply being sent");

		// The timer thread has taken its snapshot and is about to write it...
		var flush = Task.Run(drafts.Flush);
		Assert.True(gated.SaveEntered.Wait(TimeSpan.FromSeconds(5)));

		// ...when the user hits Send: the UI thread discards the draft.
		var discard = Task.Run(() => drafts.Discard("A1"));
		Thread.Sleep(100);
		gated.ReleaseSave.Set();
		Task.WaitAll([flush, discard], TimeSpan.FromSeconds(10));

		Assert.Null(_store.LoadItemDraft("s.txt", "A1"));
	}

	// -- M3: an unreadable ledger is not an empty one -------------------------------------------------------------------------------------

	[Fact]
	public void Prune_WhenTheLedgerWasNotReadable_DeletesNothing_AndWhenItWas_DeletesTheClosedOnes()
	{
		_store.SaveItemDraft("s.txt", "A1", "one");
		_store.SaveItemDraft("s.txt", "A2", "two");
		using var drafts = new FocusReplyDrafts(_store, "s.txt", TimeSpan.FromMinutes(5));

		drafts.Prune([], ledgerReadable: false);
		Assert.Equal(2, _store.ListItemDraftIds("s.txt").Count);

		drafts.Prune([], ledgerReadable: true);
		Assert.Empty(_store.ListItemDraftIds("s.txt"));
	}

	// R1: a stale or closed card must not lose its draft while the ledger is unreadable (every card looks stale then).
	[Fact]
	public void DiscardClosed_WhenTheLedgerWasNotReadable_KeepsTheDraft_AndAQueuedText()
	{
		_store.SaveItemDraft("s.txt", "A1", "stored");
		using var drafts = new FocusReplyDrafts(_store, "s.txt", TimeSpan.FromMinutes(5));
		drafts.Queue("A1", "typed since");

		drafts.DiscardClosed("A1", ledgerReadable: false);
		drafts.Flush();
		Assert.Equal("typed since", _store.LoadItemDraft("s.txt", "A1"));

		drafts.DiscardClosed("A1", ledgerReadable: true);
		Assert.Null(_store.LoadItemDraft("s.txt", "A1"));
	}

	[Fact]
	public void ACorruptLedgerFile_IsReportedUnreadable_UntilItIsWrittenAgain()
	{
		using var env = new AttentionTestEnv();
		var node = AttentionTestEnv.Node("corrupt-node");
		Directory.CreateDirectory(env.Root);
		File.WriteAllText(Path.Combine(env.Root, node.NodeId + Constants.Attention.LedgerFileExtension), "{ this is not json");

		Assert.Empty(env.Service.GetItems(node.NodeId));
		Assert.False(env.Service.IsLedgerReadable(node.NodeId));

		env.Service.Post(node, [AttentionTestEnv.Req("fresh")]);
		Assert.True(env.Service.IsLedgerReadable(node.NodeId));
	}

	[Fact]
	public void AMissingLedgerFile_IsReadable_ItReallyHasNoItems()
	{
		using var env = new AttentionTestEnv();

		Assert.Empty(env.Service.GetItems("never-had-one"));
		Assert.True(env.Service.IsLedgerReadable("never-had-one"));
	}

	[Fact]
	public void TheLifecycle_KeepsEveryReplyDraft_WhileTheLedgerIsUnreadable_AndCleansOnceItIsReadable()
	{
		using var env = new LifecycleEnv();
		var fake = new FakeAttentionService { LedgerReadable = false };
		using var sut = new FocusDraftLifecycle(env.Drafts, fake, env.Files, env.Registry);
		sut.Start();
		env.Drafts.SaveItemDraft(env.Session.FileName, "A1", "precious");

		fake.Raise(env.Session.NodeId);
		Assert.Equal("precious", env.Drafts.LoadItemDraft(env.Session.FileName, "A1"));

		fake.LedgerReadable = true;
		fake.Raise(env.Session.NodeId);
		Assert.Null(env.Drafts.LoadItemDraft(env.Session.FileName, "A1"));
	}

	[Fact]
	public void FocusReplyDrafts_AfterDispose_QueueWritesNothing()
	{
		var drafts = new FocusReplyDrafts(_store, "s.txt", Short);
		drafts.Dispose();

		drafts.Queue("A1", "late");
		Thread.Sleep(150);

		Assert.Null(_store.LoadItemDraft("s.txt", "A1"));
	}

	// -- The lifecycle -----------------------------------------------------------------------------------------------------------------

	private sealed class LifecycleEnv : IDisposable
	{
		private readonly string _dir = Path.Combine(Path.GetTempPath(), "focus-lifecycle-" + Guid.NewGuid().ToString("N"));

		public DraftService Drafts { get; }
		public AttentionTestEnv Attention { get; } = new();
		public SessionFileService Files { get; }
		public SessionRegistry Registry { get; }
		public SessionNodeModel Session { get; }
		public FocusDraftLifecycle Sut { get; }

		public LifecycleEnv()
		{
			Directory.CreateDirectory(_dir);
			var settings = new AppSettingsService(_dir);
			settings.Settings.SessionFilesRoot = _dir;
			Files    = new SessionFileService(settings);
			Registry = new SessionRegistry(settings, Files, new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
			Session  = Registry.AddSession(Registry.AddDirectory(Path.Combine(_dir, "proj")), "S");
			Drafts   = new DraftService(Path.Combine(_dir, "drafts"));
			Sut      = new FocusDraftLifecycle(Drafts, Attention.Service, Files, Registry);
			Sut.Start();
		}

		public AttentionItemModel Post(string title) => Attention.Service.Post(Session, [AttentionTestEnv.Req(title)]).Single().Item;

		public void Dispose()
		{
			Sut.Dispose();
			Attention.Dispose();
			Directory.Delete(_dir, recursive: true);
		}
	}

	[Fact]
	public void WhenTheSessionAnswersOrResolvesAnItem_ItsReplyDraftIsDeleted_AndOpenItemsKeepTheirs()
	{
		using var env = new LifecycleEnv();
		var answered = env.Post("first");
		var open     = env.Post("second");
		env.Drafts.SaveItemDraft(env.Session.FileName, answered.Id, "reply 1");
		env.Drafts.SaveItemDraft(env.Session.FileName, open.Id, "reply 2");

		env.Attention.Service.ResolveBySession(env.Session, answered.Id, AttentionStatus.Answered, "done");

		Assert.Null(env.Drafts.LoadItemDraft(env.Session.FileName, answered.Id));
		Assert.Equal("reply 2", env.Drafts.LoadItemDraft(env.Session.FileName, open.Id));
	}

	[Fact]
	public void WhenTheUserDismissesAnItem_ItsReplyDraftIsDeleted()
	{
		using var env = new LifecycleEnv();
		var item = env.Post("first");
		env.Drafts.SaveItemDraft(env.Session.FileName, item.Id, "reply");

		env.Attention.Service.CloseByUser(env.Session, item.Id);

		Assert.Null(env.Drafts.LoadItemDraft(env.Session.FileName, item.Id));
	}

	[Fact]
	public void AReplyDraftOfAnItemThatIsNotInTheLedger_IsDeletedOnTheNextLedgerChange()
	{
		using var env = new LifecycleEnv();
		env.Drafts.SaveItemDraft(env.Session.FileName, "A99", "stray");

		env.Post("anything");

		Assert.Null(env.Drafts.LoadItemDraft(env.Session.FileName, "A99"));
	}

	[Fact]
	public void WhenTheSessionIsDeleted_AllItsReplyDraftsGoWithIt()
	{
		using var env = new LifecycleEnv();
		env.Drafts.SaveDraft(env.Session.FileName, "composer");
		env.Drafts.SaveItemDraft(env.Session.FileName, "A1", "one");
		env.Drafts.SaveItemDraft(env.Session.FileName, "A2", "two");
		env.Drafts.SaveItemDraft("other.txt", "A1", "kept");

		env.Files.DeleteSessionFile(env.Session.FileName);

		Assert.Empty(env.Drafts.ListItemDraftIds(env.Session.FileName));
		Assert.Null(env.Drafts.LoadDraft(env.Session.FileName));
		Assert.Equal("kept", env.Drafts.LoadItemDraft("other.txt", "A1"));
	}

	[Fact]
	public void AfterDispose_TheLifecycleDeletesNothing()
	{
		using var env = new LifecycleEnv();
		var item = env.Post("first");
		env.Drafts.SaveItemDraft(env.Session.FileName, item.Id, "reply");
		env.Sut.Dispose();

		env.Attention.Service.CloseByUser(env.Session, item.Id);

		Assert.Equal("reply", env.Drafts.LoadItemDraft(env.Session.FileName, item.Id));
	}
}
