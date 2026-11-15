using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Package J2: <c>draft.changed</c> from the draft service's writers (local stream only, coalesced) and <c>session.changed</c> when the auth error changes.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ComposerPublisherTests : IDisposable
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(8);

	private readonly string _dir;
	private readonly DraftService _drafts;
	private readonly SessionAuthErrorTracker _authErrors = new();
	private readonly RemoteEventBus _bus = new();
	private readonly RemoteNotificationPublisher _sut;
	private readonly List<RemoteNotification> _received = [];
	private readonly SessionNodeModel _node = new() { Name = "S", FileName = "s.txt", NodeId = "node-1" };
	private readonly DraftRevisions _revisions = new();

	public ComposerPublisherTests()
	{
		_dir = Path.Combine(Path.GetTempPath(), "cm_test_composer_publisher_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_dir);
		var settings = new AppSettingsService(_dir);
		settings.Settings.SessionFilesRoot = _dir;
		var files = new SessionFileService(settings);
		_drafts = new DraftService(Path.Combine(_dir, "drafts"));

		var daemon = FakeDaemonProxy.Create(out var fake);
		fake.ByFileName[_node.FileName] = _node;
		fake.ByNodeId[_node.NodeId] = _node;

		_bus.Published += n => { lock (_received) _received.Add(n); };
		using var epochs = new SessionEpochTracker(files);
		_sut = new RemoteNotificationPublisher(daemon, files, _bus, new TreeVersionTracker(), epochs, new FakeStateBuilder(), authErrors: _authErrors, drafts: _drafts);
		_sut.Start();
	}

	public void Dispose()
	{
		_sut.Stop();
		_bus.Dispose();
		Directory.Delete(_dir, recursive: true);
	}

	private List<RemoteNotification> WaitFor(string method, int count)
	{
		var sw = Stopwatch.StartNew();
		while (sw.Elapsed < Wait)
		{
			var found = Received(method);
			if (found.Count >= count)
				return found;
			Thread.Sleep(20);
		}

		return Received(method);
	}

	private List<RemoteNotification> Received(string method)
	{
		lock (_received)
			return _received.Where(n => n.Method == method).ToList();
	}

	[Fact]
	public void ASavedDraft_PublishesDraftChangedWithItsRevision()
	{
		_drafts.SaveDraft(_node.FileName, "hello");

		var notification = Assert.Single(WaitFor("draft.changed", 1));
		Assert.Equal("node-1", notification.Params["nodeId"]!.GetValue<string>());
		Assert.Equal(_revisions.Of("hello"), notification.Params["revision"]!.GetValue<string>());
	}

	// FR.23.19: the notification of a Focus reply draft carries the itemId; the composer draft's notification has no such field (additive).
	[Fact]
	public void AFocusReplyDraft_PublishesDraftChangedWithItsItemId_AndNoSessionStateRefresh()
	{
		_drafts.SaveItemDraft(_node.FileName, "A1", "reply");

		var notification = Assert.Single(WaitFor("draft.changed", 1));
		Assert.Equal("node-1", notification.Params["nodeId"]!.GetValue<string>());
		Assert.Equal("A1", notification.Params["itemId"]!.GetValue<string>());
		Assert.Equal(_revisions.Of("reply"), notification.Params["revision"]!.GetValue<string>());
		Thread.Sleep(400);
		Assert.Empty(Received("session.changed"));
	}

	[Fact]
	public void TheComposerDraftNotification_HasNoItemIdField()
	{
		_drafts.SaveDraft(_node.FileName, "hello");

		var notification = Assert.Single(WaitFor("draft.changed", 1));
		Assert.False(((System.Text.Json.Nodes.JsonObject)notification.Params).ContainsKey("itemId"));
	}

	[Fact]
	public void ReplyDraftsOfTwoItems_AreNotCoalescedIntoOne()
	{
		_drafts.SaveItemDraft(_node.FileName, "A1", "one");
		_drafts.SaveItemDraft(_node.FileName, "A2", "two");

		var all = WaitFor("draft.changed", 2);
		Assert.Equal(["A1", "A2"], all.Select(n => n.Params["itemId"]!.GetValue<string>()).Order().ToArray());
	}

	[Fact]
	public void ADeletedDraft_PublishesTheEmptyRevision()
	{
		_drafts.SaveDraft(_node.FileName, "hello");
		WaitFor("draft.changed", 1);

		_drafts.DeleteDraft(_node.FileName);

		var all = WaitFor("draft.changed", 2);
		Assert.Equal(RemoteProtocol.Composer.EmptyDraftRevision, all[^1].Params["revision"]!.GetValue<string>());
	}

	[Fact]
	public void ABurstOfSaves_IsCoalescedIntoOneNotificationWithTheLastRevision()
	{
		for (var i = 0; i < 20; i++)
			_drafts.SaveDraft(_node.FileName, "text " + i);

		var notification = Assert.Single(WaitFor("draft.changed", 1));
		Thread.Sleep(600);
		Assert.Single(Received("draft.changed"));
		Assert.Equal(_revisions.Of("text 19"), notification.Params["revision"]!.GetValue<string>());
	}

	[Fact]
	public void ADeleteOfAMissingDraft_PublishesNothing()
	{
		_drafts.DeleteDraft(_node.FileName);

		Thread.Sleep(600);
		Assert.Empty(Received("draft.changed"));
	}

	[Fact]
	public void ADraftOfAnUnknownSessionFile_PublishesNothing()
	{
		_drafts.SaveDraft("orphan.txt", "x");

		Thread.Sleep(600);
		Assert.Empty(Received("draft.changed"));
	}

	[Fact]
	public void DraftChanged_IsLocalOnly_TheRelayFiltersItOut()
	{
		_drafts.SaveDraft(_node.FileName, "x");
		var notification = Assert.Single(WaitFor("draft.changed", 1));

		Assert.True(RemoteProtocol.TurnStream.IsLocalOnly(notification));
	}

	[Fact]
	public void ADraftWrite_AlsoRefreshesTheSessionState_BecauseHasDraftMayHaveFlipped()
	{
		_drafts.SaveDraft(_node.FileName, "hello");
		Assert.Single(WaitFor("session.changed", 1));

		_drafts.DeleteDraft(_node.FileName);

		Assert.Equal("node-1", WaitFor("session.changed", 1)[0].Params["state"]!["id"]!.GetValue<string>());
	}

	[Fact]
	public void ADraftOfAnUnknownSessionFile_RefreshesNoState()
	{
		_drafts.SaveDraft("orphan.txt", "x");

		Thread.Sleep(600);
		Assert.Empty(Received("session.changed"));
	}

	[Fact]
	public void AnAuthErrorChange_PublishesSessionChanged()
	{
		_authErrors.Set("node-1", true);

		Assert.Single(WaitFor("session.changed", 1));
	}

	[Fact]
	public void AfterStop_NothingIsPublishedAnyMore()
	{
		_sut.Stop();

		_drafts.SaveDraft(_node.FileName, "x");
		_authErrors.Set("node-1", true);

		Thread.Sleep(600);
		Assert.Empty(Received("draft.changed"));
		Assert.Empty(Received("session.changed"));
	}
}
