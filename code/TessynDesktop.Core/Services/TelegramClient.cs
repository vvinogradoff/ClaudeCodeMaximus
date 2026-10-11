using System;
using System.Collections.Generic;
using System.IO;
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
			var updateId = update.GetProperty("update_id").GetInt64();
			result.Add(ParseUpdate(update, updateId) ?? TelegramUpdate.Ignored(updateId));
		}

		return result;
	}

	/// <summary>Returns null for anything the bridge does not act on (acknowledged via <see cref="TelegramUpdate.Ignored"/>, FR.21.12).</summary>
	private static TelegramUpdate? ParseUpdate(JsonElement update, long updateId)
	{
		if (!update.TryGetProperty("message", out var message))
			return null;
		if (!message.TryGetProperty("from", out var from) || !message.TryGetProperty("chat", out var chat))
			return null;

		var attachments = ParseAttachments(message);
		string? text = message.TryGetProperty("text", out var textElement) ? textElement.GetString() : null;
		if (text is null && attachments.Count == 0)
			return null; // sticker, location, poll, ... — nothing the bridge can route.

		if (text is null && message.TryGetProperty("caption", out var captionElement))
			text = captionElement.GetString();

		int? messageThreadId = message.TryGetProperty("message_thread_id", out var threadElement)
			? threadElement.GetInt32()
			: null;
		string? mediaGroupId = message.TryGetProperty("media_group_id", out var groupElement)
			? groupElement.GetString()
			: null;

		return new TelegramUpdate(
			updateId,
			chat.GetProperty("id").GetInt64(),
			from.GetProperty("id").GetInt64(),
			text ?? string.Empty,
			messageThreadId,
			attachments.Count > 0 ? attachments : null,
			mediaGroupId);
	}

	private static List<TelegramAttachment> ParseAttachments(JsonElement message)
	{
		var attachments = new List<TelegramAttachment>();

		if (message.TryGetProperty("photo", out var photo) && photo.ValueKind == JsonValueKind.Array)
		{
			// Several resolutions of the same picture — take the largest.
			JsonElement? best = null;
			long bestWeight = -1;
			foreach (var size in photo.EnumerateArray())
			{
				var area = (size.TryGetProperty("width", out var w) ? w.GetInt64() : 0)
					* (size.TryGetProperty("height", out var h) ? h.GetInt64() : 0);
				var weight = area > 0 ? area : (size.TryGetProperty("file_size", out var fs) ? fs.GetInt64() : 0);
				if (weight > bestWeight)
				{
					bestWeight = weight;
					best = size;
				}
			}

			if (best is { } chosen)
				attachments.Add(ParseFile(TelegramAttachmentKind.Photo, chosen));
		}

		// A GIF/animation message carries both `animation` and a backward-compat `document` for the
		// same file — only take it once.
		var hasAnimation = message.TryGetProperty("animation", out var animation);
		if (hasAnimation)
			attachments.Add(ParseFile(TelegramAttachmentKind.Animation, animation));
		if (!hasAnimation && message.TryGetProperty("document", out var document))
			attachments.Add(ParseFile(TelegramAttachmentKind.Document, document));
		if (message.TryGetProperty("video", out var video))
			attachments.Add(ParseFile(TelegramAttachmentKind.Video, video));
		if (message.TryGetProperty("audio", out var audio))
			attachments.Add(ParseFile(TelegramAttachmentKind.Audio, audio));
		if (message.TryGetProperty("voice", out var voice))
			attachments.Add(ParseFile(TelegramAttachmentKind.Voice, voice));
		if (message.TryGetProperty("video_note", out var videoNote))
			attachments.Add(ParseFile(TelegramAttachmentKind.VideoNote, videoNote));

		return attachments;
	}

	private static TelegramAttachment ParseFile(TelegramAttachmentKind kind, JsonElement file) =>
		new(
			kind,
			file.GetProperty("file_id").GetString() ?? string.Empty,
			file.TryGetProperty("file_unique_id", out var unique) ? unique.GetString() ?? string.Empty : string.Empty,
			file.TryGetProperty("file_name", out var name) ? name.GetString() : null,
			file.TryGetProperty("mime_type", out var mime) ? mime.GetString() : null,
			file.TryGetProperty("file_size", out var size) ? size.GetInt64() : null);

	public async Task<TelegramFileInfo> GetFileAsync(string fileId, CancellationToken ct = default)
	{
		var payload = new { file_id = fileId };
		using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
		using var response = await _httpClient.PostAsync(BuildUrl("getFile"), content, ct);

		try
		{
			using var doc = await EnsureOkAsync(response, ct);
			var result = doc.RootElement.GetProperty("result");
			var filePath = result.TryGetProperty("file_path", out var pathElement) ? pathElement.GetString() : null;
			if (string.IsNullOrEmpty(filePath))
				throw new InvalidOperationException("Telegram returned no file_path (file unavailable).");

			return new TelegramFileInfo(filePath, result.TryGetProperty("file_size", out var size) ? size.GetInt64() : null);
		}
		catch (InvalidOperationException ex) when (ex is not TelegramFileTooBigException
			&& ex.Message.Contains("too big", StringComparison.OrdinalIgnoreCase))
		{
			throw new TelegramFileTooBigException(ex.Message);
		}
	}

	public async Task DownloadFileAsync(string filePath, string destinationPath, CancellationToken ct = default)
	{
		var token = _tokenStore.Load();
		if (string.IsNullOrEmpty(token))
			throw new InvalidOperationException("Telegram bot token is not configured.");

		// The URL contains the bot token — never put it in an exception message or log.
		var url = $"{Constants.Telegram.BotApiFileBaseUrl}{token}/{filePath}";
		using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
		if (!response.IsSuccessStatusCode)
			throw new InvalidOperationException($"Telegram file download failed: HTTP {(int)response.StatusCode}");

		try
		{
			await using var source = await response.Content.ReadAsStreamAsync(ct);
			await using var target = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
			await source.CopyToAsync(target, ct);
		}
		catch
		{
			try { File.Delete(destinationPath); } catch { /* best effort */ }
			throw;
		}
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
