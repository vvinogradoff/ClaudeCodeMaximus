using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.23.19 in the WPF Focus view (TC.FD.12 to 15): the reply drafts go through <c>draft.get/set</c> with the item id, over the composer's synchronizer.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusReplyDraftTests
{
	private const string Node = "n";

	private readonly FakeTessynClient _client = new();

	private FocusViewModel Create(TimeSpan? delay = null)
	{
		var focus = new FocusViewModel(_client, new ImmediateUiDispatcher(), NullLogger<FocusViewModel>.Instance);
		focus.ReplySync.Delay = delay ?? TimeSpan.Zero;
		return focus;
	}

	private void SeedOpen(params string[] ids) => _client.Attention[Node] =
	[
		.. ids.Select((id, i) => new AttentionItemDto
		{
			Id = id, Number = i + 1, Kind = AttentionKind.Ask, Status = AttentionStatus.Open, Title = "Title " + id, CreatedUtc = DateTimeOffset.UnixEpoch.AddMinutes(i),
		}),
	];

	private static async Task WaitForAsync(Func<bool> condition, string what)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!condition() && DateTime.UtcNow < deadline)
			await Task.Delay(10);

		Assert.True(condition(), "Not reached in time: " + what);
	}

	private int Count(string prefix) => _client.Calls.Count(c => c.StartsWith(prefix, StringComparison.Ordinal));

	[Fact]
	public async Task TypingInAReplyBox_IsSavedAsThatItemsDraft_AfterThePause()
	{
		SeedOpen("A1", "A2");
		using var focus = Create();
		await focus.SetSessionAsync(Node);

		focus.Items.Single(i => i.Id == "A2").ReplyText = "my answer";

		await WaitForAsync(() => _client.ReplyDraftTexts.GetValueOrDefault((Node, "A2")) == "my answer", "saved");
		Assert.False(_client.ReplyDraftTexts.ContainsKey((Node, "A1")));
		Assert.Empty(_client.DraftTexts);   // the composer draft is not touched
	}

	[Fact]
	public async Task ACardShowingAgain_RestoresItsDraft_WithoutWritingItBack()
	{
		SeedOpen("A1");
		_client.ReplyDraftTexts[(Node, "A1")] = "kept across the restart";
		using var focus = Create();

		await focus.SetSessionAsync(Node);

		await WaitForAsync(() => focus.Items.Single().ReplyText == "kept across the restart", "restored");
		await Task.Delay(100);
		Assert.Equal(0, Count("draft.set:"));
		Assert.Equal(1, focus.AnswerCount);   // it counts for "Send answers" like typed text
	}

	[Fact]
	public async Task ATextTypedInOneRun_IsThereInTheNextRun()
	{
		SeedOpen("A1");
		using (var first = Create())
		{
			await first.SetSessionAsync(Node);
			first.Items.Single().ReplyText = "typed before closing";
			await WaitForAsync(() => _client.ReplyDraftTexts.ContainsKey((Node, "A1")), "saved");
		}

		using var second = Create();
		await second.SetSessionAsync(Node);

		await WaitForAsync(() => second.Items.Single().ReplyText == "typed before closing", "restored in the new run");
	}

	[Fact]
	public async Task TheExitFlush_WritesWhatIsStillWaitingForItsPause()
	{
		SeedOpen("A1");
		using var focus = Create(TimeSpan.FromMinutes(5));
		await focus.SetSessionAsync(Node);
		focus.Items.Single().ReplyText = "typed in the last moment";
		Assert.False(_client.ReplyDraftTexts.ContainsKey((Node, "A1")));

		await focus.FlushDraftsAsync();

		Assert.Equal("typed in the last moment", _client.ReplyDraftTexts[(Node, "A1")]);
	}

	[Fact]
	public async Task LeavingTheSession_WritesThePendingText_AtOnce()
	{
		SeedOpen("A1");
		using var focus = Create(TimeSpan.FromMinutes(5));
		await focus.SetSessionAsync(Node);
		focus.Items.Single().ReplyText = "half typed";

		await focus.SetSessionAsync("other");

		await WaitForAsync(() => _client.ReplyDraftTexts.GetValueOrDefault((Node, "A1")) == "half typed", "flushed on leaving");
	}

	[Fact]
	public async Task ASentReply_IsNeverWrittenAsADraft_EvenWhenItWasStillWaiting()
	{
		SeedOpen("A1");
		using var focus = Create(TimeSpan.FromMinutes(5));
		await focus.SetSessionAsync(Node);
		var card = focus.Items.Single();
		card.ReplyText = "the answer";

		await card.ReplyCommand.ExecuteAsync(null);
		await focus.FlushDraftsAsync();

		Assert.Equal(1, Count("attention.reply:"));
		Assert.Equal("", card.ReplyText);
		Assert.Equal(0, Count("draft.set:"));
		Assert.Empty(_client.ReplyDraftTexts);
	}

	[Fact]
	public async Task ADismissedItem_DropsItsWaitingDraft()
	{
		SeedOpen("A1");
		using var focus = Create(TimeSpan.FromMinutes(5));
		await focus.SetSessionAsync(Node);
		var card = focus.Items.Single();
		card.ReplyText = "never mind";

		await card.DismissCommand.ExecuteAsync(null);
		await focus.FlushDraftsAsync();

		Assert.Equal(0, Count("draft.set:"));
	}

	[Fact]
	public async Task AnItemTheDaemonRefusesDraftsFor_IsDroppedNotRetriedForever()
	{
		SeedOpen("A1");
		using var focus = Create();
		await focus.SetSessionAsync(Node);
		_client.ClosedReplyItems.Add((Node, "A1"));   // it closed under us

		focus.Items.Single().ReplyText = "too late";
		await WaitForAsync(() => Count("draft.set:") >= 1, "one attempt");
		await Task.Delay(200);

		Assert.Equal(1, Count("draft.set:"));
		Assert.False(focus.ReplySync.HasPending(FocusViewModel.KeyOf(Node, "A1")));
	}

	[Fact]
	public async Task AChangeOfAnotherClient_ReplacesTheBoxText_WhenNothingWasTypedHere()
	{
		SeedOpen("A1");
		using var focus = Create();
		await focus.SetSessionAsync(Node);
		await WaitForAsync(() => Count("draft.get:n:A1") == 1, "first read");

		_client.ReplyDraftTexts[(Node, "A1")] = "from the phone";
		_client.RaiseNotification(RemoteProtocol.Composer.DraftChangedEvent, new DraftChangedPayload(Node, "other-revision", "A1"));

		await WaitForAsync(() => focus.Items.Single().ReplyText == "from the phone", "replaced");
		Assert.Equal(0, Count("draft.set:"));   // no echo back
	}

	[Fact]
	public async Task OurOwnWritesEcho_DoesNotCauseAReadBack()
	{
		SeedOpen("A1");
		using var focus = Create();
		await focus.SetSessionAsync(Node);
		focus.Items.Single().ReplyText = "echo me";
		await WaitForAsync(() => focus.ReplySync.IsKnownRevision(FocusViewModel.KeyOf(Node, "A1"), FakeTessynClient.RevisionOf("echo me")), "revision recorded");
		var reads = Count("draft.get:");

		_client.RaiseNotification(RemoteProtocol.Composer.DraftChangedEvent, new DraftChangedPayload(Node, FakeTessynClient.RevisionOf("echo me"), "A1"));
		await Task.Delay(100);

		Assert.Equal(reads, Count("draft.get:"));
	}

	[Fact]
	public async Task ADraftEventWithoutAnItemId_IsTheComposersAndLeavesTheCardsAlone()
	{
		SeedOpen("A1");
		using var focus = Create();
		await focus.SetSessionAsync(Node);
		await WaitForAsync(() => Count("draft.get:n:A1") == 1, "first read");

		_client.RaiseNotification(RemoteProtocol.Composer.DraftChangedEvent, new DraftChangedPayload(Node, "composer-revision"));
		await Task.Delay(100);

		Assert.Equal(1, Count("draft.get:n:A1"));
	}

	[Fact]
	public async Task AResync_ReadsEveryCardsDraftAgain()
	{
		SeedOpen("A1");
		using var focus = Create();
		await focus.SetSessionAsync(Node);
		await WaitForAsync(() => Count("draft.get:n:A1") == 1, "first read");

		_client.ReplyDraftTexts[(Node, "A1")] = "written while disconnected";
		_client.RaiseResync();

		await WaitForAsync(() => focus.Items.SingleOrDefault()?.ReplyText == "written while disconnected", "read again after the resync");
	}

	[Fact]
	public async Task AConflictingWrite_IsMerged_NotLost()
	{
		SeedOpen("A1");
		using var focus = Create(TimeSpan.FromMinutes(5));
		await focus.SetSessionAsync(Node);
		await WaitForAsync(() => Count("draft.get:n:A1") == 1, "first read");
		var card = focus.Items.Single();
		card.ReplyText = "mine";

		_client.ReplyDraftTexts[(Node, "A1")] = "theirs";   // another client wrote meanwhile; this client's base revision is stale
		await focus.FlushDraftsAsync();

		var stored = _client.ReplyDraftTexts[(Node, "A1")];
		Assert.Contains("mine", stored);
		Assert.Contains("theirs", stored);
	}
}
