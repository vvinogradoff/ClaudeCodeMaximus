using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>File naming, extension inference, turn-text lines and retention (TC.21.7; FR.21.14, FR.21.15).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TelegramInboxTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), $"tg-inbox-tests-{Guid.NewGuid():N}");
	private readonly FakeTelegramClientForBridge _client = new();
	private DateTime _now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

	public void Dispose()
	{
		if (Directory.Exists(_root))
			Directory.Delete(_root, recursive: true);
	}

	private TelegramInbox MakeInbox() => new(_client, _root, () => _now);

	private static TelegramAttachment Doc(string? name, string? mime = null, long? size = 10, string id = "d1") =>
		new(TelegramAttachmentKind.Document, id, "uniq-" + id, name, mime, size);

	[Theory]
	[InlineData("report.PDF", null, "files/x", ".pdf")]            // original name wins
	[InlineData(null, "image/png", "files/x.bin", ".png")]         // then MIME
	[InlineData(null, "audio/ogg; codecs=opus", "files/x", ".ogg")] // MIME parameters ignored
	[InlineData(null, "application/x-unknown", "files/doc_7.txt", ".txt")] // then file_path
	[InlineData(null, null, "files/noext", "")]
	[InlineData("weird.e x!", null, "files/y.zip", ".zip")]       // unsafe extension rejected → next source
	public void InferExtension_NameThenMimeThenFilePath(string? name, string? mime, string filePath, string expected) =>
		Assert.Equal(expected, TelegramInbox.InferExtension(Doc(name, mime), filePath));

	[Fact]
	public void InferExtension_Photo_IsAlwaysJpg() =>
		Assert.Equal(".jpg", TelegramInbox.InferExtension(
			new TelegramAttachment(TelegramAttachmentKind.Photo, "p", "up", null, null, 1), "photos/file_9.png"));

	[Fact]
	public void BuildFileName_UsesUpdateIdIndexAndOriginalName()
	{
		Assert.Equal("77-2-holiday plan.pdf", TelegramInbox.BuildFileName(77, 2, Doc("holiday plan.pdf"), "files/x"));
	}

	[Fact]
	public void BuildFileName_NoOriginalName_UsesFileUniqueId()
	{
		Assert.Equal("5-0-uniq-d1.png", TelegramInbox.BuildFileName(5, 0, Doc(null, "image/png"), "files/x"));
	}

	[Theory]
	[InlineData("..\\..\\evil.exe", "9-0-evil.exe")]
	[InlineData("a/b/c.txt", "9-0-c.txt")]
	[InlineData("na:me?*<>|.txt", "9-0-na_me_____.txt")]
	public void BuildFileName_SanitizesPathAndInvalidChars(string original, string expected) =>
		Assert.Equal(expected, TelegramInbox.BuildFileName(9, 0, Doc(original), "files/x"));

	[Fact]
	public void BuildFileName_LongName_IsCapped()
	{
		var name = TelegramInbox.BuildFileName(1, 0, Doc(new string('a', 300) + ".pdf"), "files/x");
		Assert.True(name.Length < 100);
		Assert.EndsWith(".pdf", name);
	}

	[Fact]
	public async Task DownloadAsync_StoresUnderDateFolder_AbsolutePath_AndCollisionFree()
	{
		var inbox = MakeInbox();

		var first = await inbox.DownloadAsync(5, new[] { Doc("a.txt") });
		var second = await inbox.DownloadAsync(5, new[] { Doc("a.txt") }); // same update id/index/name

		var p1 = first.Single()["[attachment: ".Length..^1];
		var p2 = second.Single()["[attachment: ".Length..^1];
		Assert.NotEqual(p1, p2);
		Assert.Equal(Path.Combine(_root, "2026-10-05", "5-0-a.txt"), p1);
		Assert.Equal(Path.Combine(_root, "2026-10-05", "5-0-a-1.txt"), p2);
		Assert.True(File.Exists(p1) && File.Exists(p2));
		Assert.True(Path.IsPathRooted(p1));
	}

	[Fact]
	public async Task DownloadAsync_MultipleAttachments_IndexedInOrder()
	{
		var lines = await MakeInbox().DownloadAsync(8, new[] { Doc("a.txt", id: "x"), Doc("b.txt", id: "y") });

		Assert.EndsWith("8-0-a.txt]", lines[0]);
		Assert.EndsWith("8-1-b.txt]", lines[1]);
	}

	[Fact]
	public async Task DownloadAsync_Over20MBByTelegramMetadata_TooLargeLine_MegabytesRounded()
	{
		var lines = await MakeInbox().DownloadAsync(1, new[] { Doc("big.bin", size: 21L * 1024 * 1024) });

		Assert.Equal("[attachment too large for Telegram bot download (21 MB > 20 MB): big.bin — send it from the desktop instead]", lines.Single());
		Assert.Empty(_client.Downloads);
	}

	[Fact]
	public async Task DownloadAsync_Exactly20MB_IsDownloaded()
	{
		var lines = await MakeInbox().DownloadAsync(1, new[] { Doc("edge.bin", size: 20L * 1024 * 1024) });

		Assert.StartsWith("[attachment: ", lines.Single());
	}

	[Fact]
	public async Task DownloadAsync_GetFileReportsOver20MB_TooLargeLine()
	{
		_client.Files["d1"] = new TelegramFileInfo("files/big", 30L * 1024 * 1024);

		var lines = await MakeInbox().DownloadAsync(1, new[] { Doc("big.bin", size: null) });

		Assert.Contains("(30 MB > 20 MB): big.bin", lines.Single());
	}

	[Fact]
	public async Task DownloadAsync_Failure_ProducesFailureLine_ShortSingleLineReason()
	{
		_client.DownloadError = new InvalidOperationException("boom\nsecond line " + new string('x', 300));

		var lines = await MakeInbox().DownloadAsync(1, new[] { Doc("a.txt") });

		var line = lines.Single();
		Assert.StartsWith("[attachment download failed: a.txt — boom second line", line);
		Assert.DoesNotContain("\n", line);
		Assert.True(line.Length < 220);
	}

	[Fact]
	public async Task DownloadAsync_FailureOfOne_DoesNotAffectOthers()
	{
		_client.GetFileErrors["bad"] = new InvalidOperationException("Telegram API call failed: Bad Request: wrong file_id");

		var lines = await MakeInbox().DownloadAsync(1, new[] { Doc("a.txt", id: "bad"), Doc("b.txt", id: "good") });

		Assert.StartsWith("[attachment download failed: a.txt — ", lines[0]);
		Assert.StartsWith("[attachment: ", lines[1]);
	}

	[Fact]
	public void BuildTurnText_TextThenLines_OrNoTextOrNoLines()
	{
		Assert.Equal("hi\nA\nB", TelegramInbox.BuildTurnText("hi", new[] { "A", "B" }));
		Assert.Equal("A\nB", TelegramInbox.BuildTurnText("", new[] { "A", "B" }));
		Assert.Equal("hi", TelegramInbox.BuildTurnText("hi", Array.Empty<string>()));
	}

	[Fact]
	public void Cleanup_DeletesFilesOlderThan30Days_KeepsRecent_RemovesEmptyFolders()
	{
		var oldDir = Path.Combine(_root, "2026-08-01");
		var mixedDir = Path.Combine(_root, "2026-09-10");
		Directory.CreateDirectory(oldDir);
		Directory.CreateDirectory(mixedDir);
		var oldFile = Path.Combine(oldDir, "1-0-a.jpg");
		var staleFile = Path.Combine(mixedDir, "2-0-b.jpg");
		var freshFile = Path.Combine(mixedDir, "3-0-c.jpg");
		foreach (var f in new[] { oldFile, staleFile, freshFile })
			File.WriteAllText(f, "x");
		File.SetLastWriteTimeUtc(oldFile, _now.AddDays(-65));
		File.SetLastWriteTimeUtc(staleFile, _now.AddDays(-31));
		File.SetLastWriteTimeUtc(freshFile, _now.AddDays(-29));

		MakeInbox().CleanupOlderThan(TimeSpan.FromDays(30));

		Assert.False(File.Exists(oldFile));
		Assert.False(Directory.Exists(oldDir));
		Assert.False(File.Exists(staleFile));
		Assert.True(File.Exists(freshFile));
	}

	[Fact]
	public void Cleanup_MissingRoot_DoesNotThrow() => MakeInbox().CleanupOlderThan(TimeSpan.FromDays(30));

	[Fact]
	public void DefaultRootDirectory_IsUnderAppDataTessynDesktop() =>
		Assert.EndsWith(Path.Combine("TessynDesktop", "telegram-inbox"), TelegramInbox.DefaultRootDirectory());
}
