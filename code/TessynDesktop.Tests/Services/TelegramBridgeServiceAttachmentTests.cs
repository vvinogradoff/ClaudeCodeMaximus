using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Update acknowledgement (FR.21.12, ISSUE.011), attachments (FR.21.13–15, FR.21.17), albums
/// (FR.21.16) and inbox retention at start (FR.21.14). Test cases: TC.21.6, TC.21.7, TC.21.8.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TelegramBridgeServiceTests
{
	private string InboxDir => Path.Combine(_tempDir, "inbox");

	private static TelegramAttachment Photo(string id = "p1") =>
		new(TelegramAttachmentKind.Photo, id, "u-" + id, null, null, 1000);

	private string[] TurnLines(int index = 0) => _daemon.RunTurnCalls[index].prompt.Split('\n');

	private void MapSession()
	{
		_daemon.AddNode(MakeNode("node-1"));
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;
	}

	// ── Update acknowledgement (TC.21.6) ────────────────────────────────────────

	[Fact]
	public async Task PollLoop_OnlyNonActionableUpdates_AdvancesAndPersistsOffsetPastMaxUpdateId()
	{
		_client.UpdateBatches.Enqueue(new[] { TelegramUpdate.Ignored(10), TelegramUpdate.Ignored(12), TelegramUpdate.Ignored(11) });

		BuildService().Start();
		await WaitUntil(() => _client.Polls.Count >= 2);

		Assert.Equal(13, _client.Polls[1].offset); // the next poll asks past everything acknowledged
		Assert.Equal(13, _appSettings.Settings.TelegramUpdateOffset);
		Assert.Empty(_daemon.RunTurnCalls);
		Assert.Empty(_client.SentMessages);
		var reloaded = new AppSettingsService(_tempDir);
		reloaded.Load();
		Assert.Equal(13, reloaded.Settings.TelegramUpdateOffset); // persisted
	}

	[Fact]
	public async Task PollLoop_FilteredChatAndUnauthorizedUpdates_StillAcknowledged()
	{
		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(20, ChatId: -9999, FromUserId: 555, Text: "wrong chat", MessageThreadId: 42),
			new TelegramUpdate(21, ChatId: -1001, FromUserId: 999, Text: "wrong user", MessageThreadId: 42),
		});

		BuildService().Start();
		await WaitUntil(() => _appSettings.Settings.TelegramUpdateOffset == 22);

		Assert.Empty(_daemon.RunTurnCalls);
	}

	[Fact]
	public async Task PollLoop_TrailingIgnoredUpdateAfterText_OffsetCoversIgnoredUpdate()
	{
		MapSession();
		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(30, -1001, 555, "hello", 42),
			TelegramUpdate.Ignored(31),
		});

		BuildService().Start();
		await WaitUntil(() => _daemon.RunTurnCalls.Count == 1);

		Assert.Equal(32, _appSettings.Settings.TelegramUpdateOffset);
	}

	[Fact]
	public async Task HandleUpdate_CaptionLooksLikeCommandWithAttachment_NotTreatedAsCommand()
	{
		MapSession();
		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(5, -1001, 555, "/stop", 42, new[] { Photo() }),
		});

		BuildService().Start();
		await WaitUntil(() => _daemon.RunTurnCalls.Count == 1);

		Assert.Null(_daemon.LastCancelNodeId);
		Assert.StartsWith("/stop\n[attachment: ", _daemon.RunTurnCalls[0].prompt);
	}

	// ── Attachments (TC.21.7) ───────────────────────────────────────────────────

	[Fact]
	public async Task HandleUpdate_AttachmentMessage_RoutesTurnWithAttachmentLines_AndEchoStillSuppressed()
	{
		MapSession();
		_daemon.AssistantReply = "Got it.";
		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(5, -1001, 555, "look at this", 42, new[] { Photo() }),
		});

		BuildService().Start();
		await WaitUntil(() => _client.SentMessages.Count > 0);

		var lines = TurnLines();
		Assert.Equal("look at this", lines[0]);
		Assert.Equal(2, lines.Length);
		Assert.StartsWith("[attachment: ", lines[1]);
		Assert.EndsWith("]", lines[1]);
		var path = lines[1]["[attachment: ".Length..^1];
		Assert.True(Path.IsPathRooted(path));
		Assert.StartsWith(InboxDir, path);
		Assert.EndsWith("5-0-u-p1.jpg", path);
		Assert.True(File.Exists(path));

		// Only the assistant reply is mirrored: the user entry (text + lines) matched the suppression.
		Assert.Single(_client.SentMessages);
		Assert.Equal("Got it.", _client.SentMessages[0].text);
	}

	[Fact]
	public async Task HandleUpdate_AttachmentOnlyMessage_TurnTextIsJustAttachmentLines()
	{
		MapSession();
		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(5, -1001, 555, "", 42, new[] { Photo() }),
		});

		BuildService().Start();
		await WaitUntil(() => _daemon.RunTurnCalls.Count == 1);

		Assert.StartsWith("[attachment: ", _daemon.RunTurnCalls[0].prompt);
		Assert.DoesNotContain("\n", _daemon.RunTurnCalls[0].prompt);
	}

	[Fact]
	public async Task HandleUpdate_AttachmentOver20MB_EmitsTooLargeLine_NoDownload()
	{
		MapSession();
		var big = new TelegramAttachment(TelegramAttachmentKind.Video, "v1", "uv1", "clip.mp4", "video/mp4", 25L * 1024 * 1024 + 300_000);
		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(5, -1001, 555, "big one", 42, new[] { big }),
		});

		BuildService().Start();
		await WaitUntil(() => _daemon.RunTurnCalls.Count == 1);

		Assert.Equal(
			"big one\n[attachment too large for Telegram bot download (25.3 MB > 20 MB): clip.mp4 — send it from the desktop instead]",
			_daemon.RunTurnCalls[0].prompt);
		Assert.Empty(_client.Downloads);
	}

	[Fact]
	public async Task HandleUpdate_GetFileSaysTooBig_EmitsTooLargeLine()
	{
		MapSession();
		_client.GetFileErrors["d1"] = new TelegramFileTooBigException("Bad Request: file is too big");
		var doc = new TelegramAttachment(TelegramAttachmentKind.Document, "d1", "ud1", "a.zip", null, null);
		_client.UpdateBatches.Enqueue(new[] { new TelegramUpdate(5, -1001, 555, "", 42, new[] { doc }) });

		BuildService().Start();
		await WaitUntil(() => _daemon.RunTurnCalls.Count == 1);

		Assert.Equal(
			"[attachment too large for Telegram bot download (? MB > 20 MB): a.zip — send it from the desktop instead]",
			_daemon.RunTurnCalls[0].prompt);
	}

	[Fact]
	public async Task HandleUpdate_DownloadFails_StillRoutesTextWithFailureLine_LoopContinues()
	{
		MapSession();
		_client.DownloadError = new InvalidOperationException("HTTP 502");
		var doc = new TelegramAttachment(TelegramAttachmentKind.Document, "d1", "ud1", "report.pdf", "application/pdf", 100);
		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(5, -1001, 555, "read this", 42, new[] { doc }),
			new TelegramUpdate(6, -1001, 555, "next message", 42),
		});

		BuildService().Start();
		await WaitUntil(() => _daemon.RunTurnCalls.Count == 2);

		var first = _daemon.RunTurnCalls.Single(c => c.prompt.StartsWith("read this"));
		Assert.Equal("read this\n[attachment download failed: report.pdf — HTTP 502]", first.prompt);
		Assert.Contains(_daemon.RunTurnCalls, c => c.prompt == "next message");
	}

	[Fact]
	public async Task HandleUpdate_AttachmentInUnmappedTopic_RepliesUnrecognized_NoDownload()
	{
		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(5, -1001, 555, "", 999, new[] { Photo() }),
		});

		BuildService().Start();
		await WaitUntil(() => _client.SentMessages.Count > 0);

		Assert.Contains("Unrecognized", _client.SentMessages[0].text);
		Assert.Empty(_client.Downloads);
	}

	// ── Albums (TC.21.8) ────────────────────────────────────────────────────────

	[Fact]
	public async Task Album_SameBatch_CoalescedIntoOneTurn_FirstCaptionWins_OrderPreserved()
	{
		MapSession();
		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(1, -1001, 555, "", 42, new[] { Photo("a") }, MediaGroupId: "g1"),
			new TelegramUpdate(2, -1001, 555, "first caption", 42, new[] { Photo("b") }, MediaGroupId: "g1"),
			new TelegramUpdate(3, -1001, 555, "second caption", 42, new[] { Photo("c") }, MediaGroupId: "g1"),
			new TelegramUpdate(4, -1001, 555, "separate", 42),
		});

		BuildService().Start();
		await WaitUntil(() => _daemon.RunTurnCalls.Count == 2);

		var album = _daemon.RunTurnCalls.Single(c => c.prompt.StartsWith("first caption"));
		var lines = album.prompt.Split('\n');
		Assert.Equal(4, lines.Length);
		Assert.EndsWith("1-0-u-a.jpg]", lines[1]);
		Assert.EndsWith("1-1-u-b.jpg]", lines[2]);
		Assert.EndsWith("1-2-u-c.jpg]", lines[3]);
		Assert.Contains(_daemon.RunTurnCalls, c => c.prompt == "separate");
		// The batch ends with a non-album update → no follow-up poll.
		Assert.DoesNotContain(_client.Polls, p => p.timeoutSeconds == Constants.Telegram.AlbumFollowUpPollSeconds);
	}

	[Fact]
	public async Task Album_SplitAcrossBatches_FollowUpPollMergesIntoOneTurn()
	{
		MapSession();
		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(1, -1001, 555, "", 42, new[] { Photo("a") }, MediaGroupId: "g1"),
			new TelegramUpdate(2, -1001, 555, "cap", 42, new[] { Photo("b") }, MediaGroupId: "g1"),
		});
		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(3, -1001, 555, "", 42, new[] { Photo("c") }, MediaGroupId: "g1"),
		});

		BuildService().Start();
		await WaitUntil(() => _daemon.RunTurnCalls.Count >= 1);
		await Task.Delay(150); // make sure no second turn shows up

		Assert.Single(_daemon.RunTurnCalls);
		Assert.Equal(4, TurnLines().Length); // caption + 3 attachments
		Assert.Equal("cap", TurnLines()[0]);
		Assert.Equal(Constants.Telegram.AlbumFollowUpPollSeconds, _client.Polls[1].timeoutSeconds);
		Assert.Equal(3, _client.Polls[1].offset); // follow-up starts from the already-advanced offset
		Assert.Equal(4, _appSettings.Settings.TelegramUpdateOffset);
	}

	[Fact]
	public async Task Album_BatchEndsWithGroup_NoMoreMessages_DispatchedAfterOneFollowUpPoll()
	{
		MapSession();
		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(1, -1001, 555, "solo", 42, new[] { Photo("a") }, MediaGroupId: "g1"),
		});

		BuildService().Start();
		await WaitUntil(() => _daemon.RunTurnCalls.Count == 1);

		Assert.Equal(1, _client.Polls.Count(p => p.timeoutSeconds == Constants.Telegram.AlbumFollowUpPollSeconds));
	}

	// ── Retention at Start() (TC.21.7) ──────────────────────────────────────────

	[Fact]
	public void Start_DeletesInboxFilesOlderThan30Days()
	{
		var oldDir = Path.Combine(InboxDir, "2020-01-01");
		var newDir = Path.Combine(InboxDir, "2099-01-01");
		Directory.CreateDirectory(oldDir);
		Directory.CreateDirectory(newDir);
		var oldFile = Path.Combine(oldDir, "1-0-a.jpg");
		var newFile = Path.Combine(newDir, "2-0-b.jpg");
		File.WriteAllText(oldFile, "x");
		File.WriteAllText(newFile, "x");
		File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-31));

		BuildService().Start();

		Assert.False(File.Exists(oldFile));
		Assert.False(Directory.Exists(oldDir));
		Assert.True(File.Exists(newFile));
	}
}
