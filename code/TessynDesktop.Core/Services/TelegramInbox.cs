using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Downloads inbound Telegram attachments into the inbox folder and renders the per-attachment
/// lines appended to a routed turn's text (FR.21.14, FR.21.15, FR.21.17). Never throws for a
/// per-attachment failure — every outcome becomes a line, so a bad download cannot lose the
/// message text or break the poll loop.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TelegramInbox
{
	private static readonly ILogger _log = Log.ForContext<TelegramInbox>();

	private const int MaxBaseNameLength = 80;
	private const int MaxReasonLength = 120;

	private static readonly Regex SafeExtension = new(@"^\.[A-Za-z0-9]{1,10}$", RegexOptions.Compiled);

	private static readonly Dictionary<string, string> ExtensionByMimeType = new(StringComparer.OrdinalIgnoreCase)
	{
		["image/jpeg"] = ".jpg",
		["image/png"] = ".png",
		["image/gif"] = ".gif",
		["image/webp"] = ".webp",
		["image/heic"] = ".heic",
		["video/mp4"] = ".mp4",
		["video/quicktime"] = ".mov",
		["video/webm"] = ".webm",
		["audio/mpeg"] = ".mp3",
		["audio/mp4"] = ".m4a",
		["audio/ogg"] = ".ogg",
		["audio/x-wav"] = ".wav",
		["audio/wav"] = ".wav",
		["application/pdf"] = ".pdf",
		["application/zip"] = ".zip",
		["application/json"] = ".json",
		["text/plain"] = ".txt",
		["text/csv"] = ".csv",
		["text/markdown"] = ".md",
	};

	private readonly ITelegramClient _client;
	private readonly string _rootDirectory;
	private readonly Func<DateTime> _utcNow;

	public TelegramInbox(ITelegramClient client, string rootDirectory, Func<DateTime>? utcNow = null)
	{
		_client = client;
		_rootDirectory = rootDirectory;
		_utcNow = utcNow ?? (() => DateTime.UtcNow);
	}

	/// <summary><c>%AppData%\TessynDesktop\telegram-inbox</c> (FR.21.14).</summary>
	public static string DefaultRootDirectory() =>
		Path.Combine(Constants.DataRoot, Constants.Telegram.InboxFolderName);

	/// <summary>Message text, then one line per attachment line (FR.21.15).</summary>
	public static string BuildTurnText(string text, IReadOnlyList<string> attachmentLines)
	{
		if (attachmentLines.Count == 0)
			return text;
		var lines = string.Join("\n", attachmentLines);
		return text.Length == 0 ? lines : text + "\n" + lines;
	}

	/// <summary>
	/// Downloads each attachment (<paramref name="updateId"/> + running index name the files) and
	/// returns one line per attachment, in order.
	/// </summary>
	public async Task<IReadOnlyList<string>> DownloadAsync(
		long updateId, IReadOnlyList<TelegramAttachment> attachments, CancellationToken ct = default)
	{
		var lines = new List<string>(attachments.Count);
		for (var index = 0; index < attachments.Count; index++)
			lines.Add(await DownloadOneAsync(updateId, index, attachments[index], ct));
		return lines;
	}

	private async Task<string> DownloadOneAsync(long updateId, int index, TelegramAttachment attachment, CancellationToken ct)
	{
		var displayName = DisplayName(attachment);

		if (attachment.FileSize is long size && size > Constants.Telegram.MaxBotDownloadBytes)
			return TooLargeLine(size, displayName);

		try
		{
			var info = await _client.GetFileAsync(attachment.FileId, ct);
			if (info.FileSize is long infoSize && infoSize > Constants.Telegram.MaxBotDownloadBytes)
				return TooLargeLine(infoSize, displayName);

			var dayDirectory = Path.Combine(_rootDirectory, _utcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
			Directory.CreateDirectory(dayDirectory);
			var path = ReserveFilePath(dayDirectory, updateId, index, attachment, info.FilePath);

			await _client.DownloadFileAsync(info.FilePath, path, ct);
			return $"[attachment: {path}]";
		}
		catch (TelegramFileTooBigException)
		{
			return TooLargeLine(attachment.FileSize, displayName);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			// Message only — never the exception's URL/stack (the download URL carries the bot token).
			_log.Warning("TelegramInbox: attachment download failed for update {UpdateId} #{Index}: {Reason}",
				updateId, index, ShortReason(ex));
			return $"[attachment download failed: {displayName} — {ShortReason(ex)}]";
		}
	}

	private static string TooLargeLine(long? size, string displayName)
	{
		var mb = size is long bytes
			? (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture)
			: "?";
		return $"[attachment too large for Telegram bot download ({mb} MB > 20 MB): {displayName} — send it from the desktop instead]";
	}

	private static string ShortReason(Exception ex)
	{
		var reason = string.Join(' ', ex.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
		if (reason.Length == 0)
			reason = ex.GetType().Name;
		return reason.Length > MaxReasonLength ? reason[..MaxReasonLength] + "…" : reason;
	}

	private static string DisplayName(TelegramAttachment attachment)
	{
		if (!string.IsNullOrWhiteSpace(attachment.FileName))
			return attachment.FileName.Replace('\r', ' ').Replace('\n', ' ').Trim();
		var kind = attachment.Kind.ToString().ToLowerInvariant();
		return string.IsNullOrEmpty(attachment.FileUniqueId) ? kind : $"{kind}-{attachment.FileUniqueId}";
	}

	// ── Naming ───────────────────────────────────────────────────────────────────

	/// <summary>
	/// <c>{updateId}-{index}-{sanitized name or file_unique_id}{ext}</c>, made collision-free by a
	/// numeric suffix, and created empty so concurrent callers cannot take the same name.
	/// </summary>
	private static string ReserveFilePath(string directory, long updateId, int index, TelegramAttachment attachment, string serverFilePath)
	{
		var fileName = BuildFileName(updateId, index, attachment, serverFilePath);
		var stem = Path.GetFileNameWithoutExtension(fileName);
		var ext = Path.GetExtension(fileName);

		for (var attempt = 0; ; attempt++)
		{
			var candidate = Path.Combine(directory, attempt == 0 ? fileName : $"{stem}-{attempt}{ext}");
			try
			{
				using (new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
				return candidate;
			}
			catch (IOException) when (File.Exists(candidate))
			{
				// Taken — try the next suffix.
			}
		}
	}

	/// <summary>The inbox file name for an attachment (no directory). Exposed for tests.</summary>
	public static string BuildFileName(long updateId, int index, TelegramAttachment attachment, string serverFilePath)
	{
		var originalStem = string.IsNullOrWhiteSpace(attachment.FileName)
			? null
			: Path.GetFileNameWithoutExtension(attachment.FileName.Replace('\\', '/').Split('/').Last());
		var stem = SanitizeStem(string.IsNullOrWhiteSpace(originalStem) ? attachment.FileUniqueId : originalStem);
		return $"{updateId}-{index}-{stem}{InferExtension(attachment, serverFilePath)}";
	}

	/// <summary>Photos → <c>.jpg</c>; otherwise original name's extension, then MIME type, then <c>file_path</c>'s extension.</summary>
	public static string InferExtension(TelegramAttachment attachment, string serverFilePath)
	{
		if (attachment.Kind == TelegramAttachmentKind.Photo)
			return ".jpg";

		if (!string.IsNullOrWhiteSpace(attachment.FileName)
			&& NormalizeExtension(Path.GetExtension(attachment.FileName.Replace('\\', '/').Split('/').Last())) is { } fromName)
			return fromName;

		if (attachment.MimeType is { } mime
			&& ExtensionByMimeType.TryGetValue(mime.Split(';')[0].Trim(), out var fromMime))
			return fromMime;

		return NormalizeExtension(Path.GetExtension(serverFilePath ?? string.Empty)) ?? string.Empty;
	}

	private static string? NormalizeExtension(string ext)
	{
		if (!SafeExtension.IsMatch(ext))
			return null;
		return ext.ToLowerInvariant();
	}

	private static string SanitizeStem(string? raw)
	{
		var invalid = Path.GetInvalidFileNameChars();
		var sb = new StringBuilder();
		foreach (var c in raw ?? string.Empty)
			sb.Append(char.IsControl(c) || Array.IndexOf(invalid, c) >= 0 || c is '/' or '\\' ? '_' : c);

		var stem = sb.ToString().Trim().Trim('.');
		if (stem.Length > MaxBaseNameLength)
			stem = stem[..MaxBaseNameLength].TrimEnd('.', ' ');
		return stem.Length == 0 ? "file" : stem;
	}

	// ── Retention ────────────────────────────────────────────────────────────────

	/// <summary>Deletes inbox files older than <paramref name="maxAge"/> and any date folders left empty (FR.21.14). Never throws.</summary>
	public void CleanupOlderThan(TimeSpan maxAge)
	{
		try
		{
			if (!Directory.Exists(_rootDirectory))
				return;

			var cutoff = _utcNow() - maxAge;
			foreach (var dir in Directory.EnumerateDirectories(_rootDirectory))
			{
				foreach (var file in Directory.EnumerateFiles(dir))
				{
					try
					{
						if (File.GetLastWriteTimeUtc(file) < cutoff)
							File.Delete(file);
					}
					catch (Exception ex)
					{
						_log.Warning(ex, "TelegramInbox: could not delete expired file {File}", file);
					}
				}

				try
				{
					if (!Directory.EnumerateFileSystemEntries(dir).Any())
						Directory.Delete(dir);
				}
				catch { /* best effort */ }
			}
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "TelegramInbox: retention cleanup failed");
		}
	}
}
