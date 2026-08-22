using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class TelegramClient : ITelegramClient
{
	private readonly HttpClient _httpClient;
	private readonly ITelegramTokenStore _tokenStore;

	public TelegramClient(HttpClient httpClient, ITelegramTokenStore tokenStore)
	{
		_httpClient = httpClient;
		_tokenStore = tokenStore;
	}

	public async Task SendMessageAsync(long chatId, string text, int? messageThreadId, CancellationToken ct = default)
	{
		var payload = messageThreadId is null
			? new { chat_id = chatId, text }
			: (object)new { chat_id = chatId, text, message_thread_id = messageThreadId.Value };

		using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
		using var response = await _httpClient.PostAsync(BuildUrl("sendMessage"), content, ct);
		await EnsureOkAsync(response, ct);
	}

	public async Task<int> CreateForumTopicAsync(long chatId, string name, CancellationToken ct = default)
	{
		var truncatedName = name.Length > Constants.Telegram.MaxTopicNameLength
			? name[..Constants.Telegram.MaxTopicNameLength]
			: name;

		var payload = new { chat_id = chatId, name = truncatedName };
		using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
		using var response = await _httpClient.PostAsync(BuildUrl("createForumTopic"), content, ct);

		using var doc = await EnsureOkAsync(response, ct);
		return doc.RootElement.GetProperty("result").GetProperty("message_thread_id").GetInt32();
	}

	public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken ct = default)
	{
		var url = $"{BuildUrl("getUpdates")}?timeout={timeoutSeconds}&offset={offset}";
		using var response = await _httpClient.GetAsync(url, ct);
		using var doc = await EnsureOkAsync(response, ct);

		var result = new List<TelegramUpdate>();
		foreach (var update in doc.RootElement.GetProperty("result").EnumerateArray())
		{
			if (!update.TryGetProperty("message", out var message))
				continue;
			if (!message.TryGetProperty("text", out var textElement))
				continue;
			if (!message.TryGetProperty("from", out var from))
				continue;

			var updateId = update.GetProperty("update_id").GetInt64();
			var chatId = message.GetProperty("chat").GetProperty("id").GetInt64();
			var fromUserId = from.GetProperty("id").GetInt64();
			var text = textElement.GetString() ?? string.Empty;
			int? messageThreadId = message.TryGetProperty("message_thread_id", out var threadElement)
				? threadElement.GetInt32()
				: null;

			result.Add(new TelegramUpdate(updateId, chatId, fromUserId, text, messageThreadId));
		}

		return result;
	}

	private string BuildUrl(string method)
	{
		var token = _tokenStore.Load();
		if (string.IsNullOrEmpty(token))
			throw new InvalidOperationException("Telegram bot token is not configured.");

		return $"{Constants.Telegram.BotApiBaseUrl}{token}/{method}";
	}

	/// <summary>Parses the response body and throws if the HTTP call or the Bot API's own "ok" flag failed.</summary>
	private static async Task<JsonDocument> EnsureOkAsync(HttpResponseMessage response, CancellationToken ct)
	{
		var body = await response.Content.ReadAsStringAsync(ct);
		var doc = JsonDocument.Parse(body);

		if (!response.IsSuccessStatusCode || !doc.RootElement.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
		{
			var description = doc.RootElement.TryGetProperty("description", out var descElement)
				? descElement.GetString()
				: body;
			doc.Dispose();
			throw new InvalidOperationException($"Telegram API call failed: {description}");
		}

		return doc;
	}
}
