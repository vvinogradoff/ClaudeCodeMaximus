using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

// ── Fakes ─────────────────────────────────────────────────────────────────────

internal sealed class FakeTelegramTokenStore : ITelegramTokenStore
{
	public string? Token { get; set; } = "test-token";
	public string? Load() => Token;
	public void Save(string token) => Token = token;
	public void Delete() => Token = null;
}

/// <summary>
/// Returns a canned response for every outgoing request, recording the last request made — the
/// request body is read eagerly here since <see cref="TelegramClient"/> disposes its
/// <see cref="StringContent"/> (a <c>using</c>) as soon as the call returns.
/// </summary>
internal sealed class FakeTelegramHttpHandler : HttpMessageHandler
{
	public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);
	public HttpRequestMessage? LastRequest { get; private set; }
	public string? LastRequestBody { get; private set; }

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		LastRequest = request;
		LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
		return Responder(request);
	}
}

// ── Tests ──────────────────────────────────────────────────────────────────────

/// <remarks>Created by Claude</remarks>
public sealed class TelegramClientTests
{
	private readonly FakeTelegramHttpHandler _handler = new();
	private readonly FakeTelegramTokenStore _tokenStore = new();

	private TelegramClient MakeClient() => new(new HttpClient(_handler), _tokenStore);

	private static HttpResponseMessage JsonResponse(string json) =>
		new(HttpStatusCode.OK) { Content = new StringContent(json) };

	[Fact]
	public async Task SendMessageAsync_IncludesChatIdAndThreadId_WhenThreadIdProvided()
	{
		_handler.Responder = _ => JsonResponse("""{"ok":true,"result":{}}""");
		var client = MakeClient();

		await client.SendMessageAsync(-100123, "hello", messageThreadId: 42);

		var body = _handler.LastRequestBody!;
		Assert.Contains("\"chat_id\":-100123", body);
		Assert.Contains("\"message_thread_id\":42", body);
		Assert.Contains("sendMessage", _handler.LastRequest!.RequestUri!.ToString());
	}

	[Fact]
	public async Task SendMessageAsync_OmitsThreadId_WhenNull()
	{
		_handler.Responder = _ => JsonResponse("""{"ok":true,"result":{}}""");
		var client = MakeClient();

		await client.SendMessageAsync(-100123, "hello", messageThreadId: null);

		Assert.DoesNotContain("message_thread_id", _handler.LastRequestBody!);
	}

	[Fact]
	public async Task CreateForumTopicAsync_ParsesMessageThreadIdFromResponse()
	{
		_handler.Responder = _ => JsonResponse("""{"ok":true,"result":{"message_thread_id":77,"name":"x"}}""");
		var client = MakeClient();

		var threadId = await client.CreateForumTopicAsync(-100123, "My Session");

		Assert.Equal(77, threadId);
		Assert.Contains("createForumTopic", _handler.LastRequest!.RequestUri!.ToString());
	}

	[Fact]
	public async Task EditForumTopicAsync_Success_PostsRequestShapeAndReturnsTrue()
	{
		_handler.Responder = _ => JsonResponse("""{"ok":true,"result":true}""");

		var renamed = await MakeClient().EditForumTopicAsync(-100123, 42, "HardStop / Fix login");

		Assert.True(renamed);
		Assert.Contains("editForumTopic", _handler.LastRequest!.RequestUri!.ToString());
		using var doc = System.Text.Json.JsonDocument.Parse(_handler.LastRequestBody!);
		Assert.Equal(-100123, doc.RootElement.GetProperty("chat_id").GetInt64());
		Assert.Equal(42, doc.RootElement.GetProperty("message_thread_id").GetInt32());
		Assert.Equal("HardStop / Fix login", doc.RootElement.GetProperty("name").GetString());
	}

	[Fact]
	public async Task EditForumTopicAsync_NameOverMaxLength_SendsTruncatedTo128()
	{
		_handler.Responder = _ => JsonResponse("""{"ok":true,"result":true}""");

		await MakeClient().EditForumTopicAsync(-100123, 42, new string('x', 300));

		using var doc = System.Text.Json.JsonDocument.Parse(_handler.LastRequestBody!);
		Assert.Equal(Constants.Telegram.MaxTopicNameLength, doc.RootElement.GetProperty("name").GetString()!.Length);
	}

	[Fact]
	public async Task EditForumTopicAsync_TopicNotModified_ReturnsFalseWithoutThrowing()
	{
		_handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
		{
			Content = new StringContent("""{"ok":false,"error_code":400,"description":"Bad Request: TOPIC_NOT_MODIFIED"}"""),
		};

		Assert.False(await MakeClient().EditForumTopicAsync(-100123, 42, "Same"));
	}

	[Fact]
	public async Task EditForumTopicAsync_OtherError_ThrowsTelegramApiExceptionWithCodeAndDescription()
	{
		_handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
		{
			Content = new StringContent("""{"ok":false,"error_code":400,"description":"Bad Request: TOPIC_ID_INVALID"}"""),
		};

		var ex = await Assert.ThrowsAsync<TelegramApiException>(() => MakeClient().EditForumTopicAsync(-100123, 42, "x"));

		Assert.Equal(400, ex.ErrorCode);
		Assert.Equal("Bad Request: TOPIC_ID_INVALID", ex.Description);
		Assert.Null(ex.RetryAfterSeconds);
	}

	[Fact]
	public async Task EditForumTopicAsync_RateLimit_ThrowsWithRetryAfterSeconds()
	{
		_handler.Responder = _ => new HttpResponseMessage((HttpStatusCode)429)
		{
			Content = new StringContent("""{"ok":false,"error_code":429,"description":"Too Many Requests: retry after 7","parameters":{"retry_after":7}}"""),
		};

		var ex = await Assert.ThrowsAsync<TelegramApiException>(() => MakeClient().EditForumTopicAsync(-100123, 42, "x"));

		Assert.Equal(429, ex.ErrorCode);
		Assert.Equal(7, ex.RetryAfterSeconds);
	}

	[Fact]
	public async Task SendMessageAsync_ApiError_StillThrowsInvalidOperationException()
	{
		_handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
		{
			Content = new StringContent("""{"ok":false,"error_code":400,"description":"Bad Request"}"""),
		};

		await Assert.ThrowsAsync<InvalidOperationException>(() => MakeClient().SendMessageAsync(-100123, "hi", null));
	}

	[Fact]
	public async Task GetUpdatesAsync_ParsesMultipleUpdates_IncludingOptionalMessageThreadId()
	{
		_handler.Responder = _ => JsonResponse("""
		{
			"ok": true,
			"result": [
				{
					"update_id": 1001,
					"message": { "message_id": 1, "date": 0, "chat": {"id": -100123}, "from": {"id": 555}, "text": "hi" }
				},
				{
					"update_id": 1002,
					"message": { "message_id": 2, "date": 0, "chat": {"id": -100123}, "from": {"id": 555}, "text": "in a topic", "message_thread_id": 42 }
				}
			]
		}
		""");
		var client = MakeClient();

		var updates = await client.GetUpdatesAsync(offset: 1000, timeoutSeconds: 30);

		Assert.Equal(2, updates.Count);
		Assert.Null(updates[0].MessageThreadId);
		Assert.Equal(42, updates[1].MessageThreadId);
		Assert.Equal(555, updates[1].FromUserId);
		Assert.Equal("in a topic", updates[1].Text);
	}

	[Fact]
	public async Task GetUpdatesAsync_ReturnsEveryUpdate_FlagsNonActionable()
	{
		// FR.21.12 — non-actionable updates are still returned so the caller acknowledges them.
		_handler.Responder = _ => JsonResponse("""
		{
			"ok": true,
			"result": [
				{ "update_id": 1, "edited_message": { "message_id": 1, "chat": {"id": 1}, "from": {"id": 1}, "text": "edit" } },
				{ "update_id": 2, "message": { "message_id": 2, "chat": {"id": 1}, "from": {"id": 1}, "sticker": {} } },
				{ "update_id": 3, "message": { "message_id": 3, "chat": {"id": 1}, "text": "no sender" } },
				{ "update_id": 4, "my_chat_member": {} },
				{ "update_id": 5, "message": { "message_id": 5, "chat": {"id": 1}, "from": {"id": 1}, "text": "ok" } }
			]
		}
		""");
		var client = MakeClient();

		var updates = await client.GetUpdatesAsync(0, 30);

		Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, updates.Select(u => u.UpdateId));
		Assert.Equal(new[] { false, false, false, false, true }, updates.Select(u => u.IsActionable));
	}

	[Fact]
	public async Task ApiCall_OkFalse_ThrowsWithDescription()
	{
		_handler.Responder = _ => JsonResponse("""{"ok":false,"description":"chat not found"}""");
		var client = MakeClient();

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => client.SendMessageAsync(1, "hi", null));
		Assert.Contains("chat not found", ex.Message);
	}

	[Fact]
	public async Task ApiCall_NoTokenConfigured_ThrowsBeforeCallingHttp()
	{
		_tokenStore.Token = null;
		var client = MakeClient();

		await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendMessageAsync(1, "hi", null));
		Assert.Null(_handler.LastRequest);
	}
}
