using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Attachment parsing, getFile and file download (TC.21.6, TC.21.7; FR.21.13, FR.21.14).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TelegramClientAttachmentTests
{
	private readonly FakeTelegramHttpHandler _handler = new();
	private readonly FakeTelegramTokenStore _tokenStore = new();

	private TelegramClient MakeClient() => new(new HttpClient(_handler), _tokenStore);

	private static HttpResponseMessage JsonResponse(string json) =>
		new(HttpStatusCode.OK) { Content = new StringContent(json) };

	[Fact]
	public async Task GetUpdatesAsync_Caption_UsedAsText_AndTextPreferredOverCaption()
	{
		_handler.Responder = _ => JsonResponse("""
		{
			"ok": true,
			"result": [
				{ "update_id": 1, "message": { "chat": {"id": 1}, "from": {"id": 1}, "caption": "a caption", "document": {"file_id":"f","file_unique_id":"u"} } },
				{ "update_id": 2, "message": { "chat": {"id": 1}, "from": {"id": 1}, "text": "t", "caption": "c", "document": {"file_id":"f","file_unique_id":"u"} } }
			]
		}
		""");

		var updates = await MakeClient().GetUpdatesAsync(0, 30);

		Assert.Equal("a caption", updates[0].Text);
		Assert.Equal("t", updates[1].Text);
	}

	[Fact]
	public async Task GetUpdatesAsync_Photo_TakesLargestSize_AndCapturesMediaGroupAndThread()
	{
		_handler.Responder = _ => JsonResponse("""
		{
			"ok": true,
			"result": [ { "update_id": 7, "message": { "chat": {"id": -100}, "from": {"id": 5}, "message_thread_id": 42, "media_group_id": "mg1",
				"photo": [
					{"file_id":"small","file_unique_id":"us","width":90,"height":60,"file_size":1000},
					{"file_id":"big","file_unique_id":"ub","width":1280,"height":853,"file_size":90000},
					{"file_id":"mid","file_unique_id":"um","width":320,"height":213,"file_size":9000}
				] } } ]
		}
		""");

		var update = (await MakeClient().GetUpdatesAsync(0, 30)).Single();

		var attachment = Assert.Single(update.Attachments!);
		Assert.Equal(TelegramAttachmentKind.Photo, attachment.Kind);
		Assert.Equal("big", attachment.FileId);
		Assert.Equal("ub", attachment.FileUniqueId);
		Assert.Equal(90000, attachment.FileSize);
		Assert.Equal("mg1", update.MediaGroupId);
		Assert.Equal(42, update.MessageThreadId);
		Assert.Equal("", update.Text);
		Assert.True(update.IsActionable);
	}

	[Theory]
	[InlineData("document", TelegramAttachmentKind.Document)]
	[InlineData("video", TelegramAttachmentKind.Video)]
	[InlineData("audio", TelegramAttachmentKind.Audio)]
	[InlineData("voice", TelegramAttachmentKind.Voice)]
	[InlineData("video_note", TelegramAttachmentKind.VideoNote)]
	[InlineData("animation", TelegramAttachmentKind.Animation)]
	public async Task GetUpdatesAsync_MediaTypes_ParsedWithNameMimeAndSize(string property, TelegramAttachmentKind kind)
	{
		_handler.Responder = _ => JsonResponse(
			"{ \"ok\": true, \"result\": [ { \"update_id\": 1, \"message\": { \"chat\": {\"id\": 1}, \"from\": {\"id\": 1}, \"" + property
			+ "\": {\"file_id\":\"f1\",\"file_unique_id\":\"u1\",\"file_name\":\"x.bin\",\"mime_type\":\"application/x\",\"file_size\":123} } } ] }");

		var update = (await MakeClient().GetUpdatesAsync(0, 30)).Single();

		var attachment = Assert.Single(update.Attachments!);
		Assert.Equal(kind, attachment.Kind);
		Assert.Equal("f1", attachment.FileId);
		Assert.Equal("x.bin", attachment.FileName);
		Assert.Equal("application/x", attachment.MimeType);
		Assert.Equal(123, attachment.FileSize);
	}

	[Fact]
	public async Task GetUpdatesAsync_AnimationWithBackCompatDocument_YieldsSingleAttachment()
	{
		_handler.Responder = _ => JsonResponse("""
		{ "ok": true, "result": [ { "update_id": 1, "message": { "chat": {"id": 1}, "from": {"id": 1},
			"animation": {"file_id":"f1","file_unique_id":"u1"}, "document": {"file_id":"f1","file_unique_id":"u1"} } } ] }
		""");

		var update = (await MakeClient().GetUpdatesAsync(0, 30)).Single();

		Assert.Equal(TelegramAttachmentKind.Animation, Assert.Single(update.Attachments!).Kind);
	}

	[Fact]
	public async Task GetFileAsync_ParsesFilePathAndSize_PostsFileId()
	{
		_handler.Responder = _ => JsonResponse("""{"ok":true,"result":{"file_id":"f1","file_path":"photos/file_1.jpg","file_size":42}}""");

		var info = await MakeClient().GetFileAsync("f1");

		Assert.Equal("photos/file_1.jpg", info.FilePath);
		Assert.Equal(42, info.FileSize);
		Assert.Contains("getFile", _handler.LastRequest!.RequestUri!.ToString());
		Assert.Contains("\"file_id\":\"f1\"", _handler.LastRequestBody!);
	}

	[Fact]
	public async Task GetFileAsync_FileTooBig_ThrowsTelegramFileTooBigException()
	{
		_handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
		{
			Content = new StringContent("""{"ok":false,"error_code":400,"description":"Bad Request: file is too big"}"""),
		};

		await Assert.ThrowsAsync<TelegramFileTooBigException>(() => MakeClient().GetFileAsync("f1"));
	}

	[Fact]
	public async Task DownloadFileAsync_UsesFileBaseUrlWithToken_WritesBodyToDestination()
	{
		_handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) };
		var dest = Path.Combine(Path.GetTempPath(), $"tg-dl-{Guid.NewGuid():N}.bin");
		try
		{
			await MakeClient().DownloadFileAsync("photos/file_1.jpg", dest);

			Assert.Equal("https://api.telegram.org/file/bottest-token/photos/file_1.jpg", _handler.LastRequest!.RequestUri!.ToString());
			Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(dest));
		}
		finally { File.Delete(dest); }
	}

	[Fact]
	public async Task DownloadFileAsync_HttpError_ThrowsWithoutLeakingToken()
	{
		_handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
		var dest = Path.Combine(Path.GetTempPath(), $"tg-dl-{Guid.NewGuid():N}.bin");

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => MakeClient().DownloadFileAsync("x/y.jpg", dest));

		Assert.DoesNotContain("test-token", ex.Message);
		Assert.False(File.Exists(dest));
	}
}
