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

	// -- The client-side helper -------------------------------------------------------------------------------------------------------

	[Fact]
	public void FocusReplyDrafts_WritesAfterThePause_AndAnEmptyTextDeletes()
	{
		using var drafts = new FocusReplyDrafts(_store, "s.txt", Short);

		drafts.Queue("A1", "typing");
		Assert.True(WaitUntil(() => _store.LoadItemDraft("s.txt", "A1") == "typing"));

		drafts.Queue("A1", "   ");
		Assert.True(WaitUntil(() => _store.LoadItemDraft("s.txt", "A1") == null));
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
