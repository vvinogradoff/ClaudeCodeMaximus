using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IFileAccessService" />
/// <remarks>
/// Policy. Always: credential and token paths are refused (<see cref="FileAccessPolicy.IsDenied"/>), also behind a link. Relay only:
/// the type allowlist, the size caps, the per-call chunk cap and the mode (<see cref="FileAccessMode"/>); every relay read is logged.
/// A loopback client reads any file a handle points to. Created by Claude.
/// </remarks>
public sealed class FileAccessService : IFileAccessService
{
	private const int MaxUtf8SequenceBytes = 4;

	private readonly ILogger _log;
	private readonly ISessionRegistry _registry;
	private readonly IAttentionService _attention;
	private readonly ISessionEntryReader _entries;
	private readonly IAppSettingsService _settings;
	private readonly string _dataRoot;
	private readonly TimeProvider _time;

	private readonly ConcurrentDictionary<string, PreviewHandle> _previews = new(StringComparer.Ordinal);

	private sealed record PreviewHandle(string NodeId, string FullPath, DateTimeOffset ExpiresUtc);

	public FileAccessService(
		ISessionRegistry registry,
		IAttentionService attention,
		ISessionEntryReader entries,
		IAppSettingsService settings,
		string? dataRoot = null,
		TimeProvider? time = null,
		ILogger? log = null)
	{
		_registry  = registry;
		_attention = attention;
		_entries   = entries;
		_settings  = settings;
		_dataRoot  = dataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Constants.AppDataFolderName);
		_time      = time ?? TimeProvider.System;
		_log       = log ?? Log.ForContext<FileAccessService>();
	}

	// ── file.preview ─────────────────────────────────────────────────────────

	/// <inheritdoc />
	public FilePreviewResult Preview(string nodeId, string entryId, string path, TdcpTransport transport)
	{
		var node = RemoteSessionLookup.Require(_registry, nodeId);
		if (!EntryIds.TryParse(entryId, out var fileId, out var ordinal))
			throw new TdcpInvalidParamsException("'entryId' is not an entry id");

		var text = _entries.GetEntryById(node.FileName, fileId, ordinal).Content;
		var wanted = path.Trim();
		if (wanted.Length == 0 || !OccursIn(text, wanted))
			throw new TdcpException("That path does not occur in the entry's text");

		string full;
		try
		{
			full = Path.GetFullPath(Path.IsPathRooted(wanted) ? wanted : Path.Combine(node.WorkingDirectory, wanted));
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			throw new TdcpException("Not a valid path");
		}

		var info = new FileInfo(full);
		if (!info.Exists)
			throw new TdcpException("File not found");

		Authorize(full, node.NodeId, node.WorkingDirectory, info.Length, transport, "preview");

		PurgeExpired();
		var handle = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
		_previews[handle] = new PreviewHandle(node.NodeId, full, _time.GetUtcNow() + Constants.FileAccess.PreviewHandleLifetime);

		var (_, mime) = FileAccessPolicy.Classify(full);
		return new FilePreviewResult(handle, info.Name, mime, info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
	}

	/// <summary>The path occurs in the text as written; slashes of both kinds are the same character, and case is ignored (Windows paths).</summary>
	private static bool OccursIn(string text, string path) =>
		text.Replace('/', '\\').Contains(path.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);

	// ── file.read ────────────────────────────────────────────────────────────

	/// <inheritdoc />
	public FileReadResult Read(string handle, long? offset, int? length, TdcpTransport transport)
	{
		if (offset is < 0)
			throw new TdcpInvalidParamsException("'offset' must not be negative");
		if (length is < 1)
			throw new TdcpInvalidParamsException("'length' must be at least 1");

		var (nodeId, fullPath) = ResolveHandle(handle);
		var info = new FileInfo(fullPath);
		if (!info.Exists)
			throw new TdcpException("File not found");

		var workingDirectory = _registry.FindModelByNodeId(nodeId)?.WorkingDirectory ?? string.Empty;
		Authorize(fullPath, nodeId, workingDirectory, info.Length, transport, "read");

		var settings  = _settings.Settings.FileAccess;
		var (kind, mime) = FileAccessPolicy.Classify(fullPath);
		var relay     = transport == TdcpTransport.Relay;
		var cap       = relay ? Math.Max(1, settings.MaxRelayChunkBytes) : Constants.FileAccess.LoopbackChunkBytes;
		var want      = Math.Min(length ?? cap, cap);
		var start     = Math.Min(offset ?? 0, info.Length);

		var (data, actualStart, count) = ReadChunk(fullPath, start, want, info.Length, kind == FileTypeClass.Text);
		var eof = actualStart + count >= info.Length;

		if (relay)
			_log.Information("Relay file.read {Name} bytes {Offset}+{Length} of {Size} (node {NodeId}, {Path})", info.Name, actualStart, count, info.Length, nodeId, fullPath);

		return new FileReadResult(
			info.Name, mime, info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
			actualStart, count,
			kind == FileTypeClass.Text ? Constants.FileAccess.EncodingUtf8 : Constants.FileAccess.EncodingBase64,
			data, eof);
	}

	private (string NodeId, string FullPath) ResolveHandle(string handle)
	{
		if (_previews.TryGetValue(handle, out var preview))
		{
			if (preview.ExpiresUtc > _time.GetUtcNow())
				return (preview.NodeId, preview.FullPath);

			_previews.TryRemove(handle, out _);
		}

		var reference = _attention.FindFileRef(handle);
		if (reference is { Ref.AbsolutePath: { Length: > 0 } path })
			return (reference.Value.NodeId, path);

		throw new TdcpException(Constants.FileAccess.UnknownHandleMessage);
	}

	/// <summary>Applies the policy for the transport; throws <see cref="TdcpException"/> and, for the relay, logs the refusal.</summary>
	private void Authorize(string fullPath, string nodeId, string workingDirectory, long size, TdcpTransport transport, string operation)
	{
		var reason = Check(fullPath, nodeId, workingDirectory, size, transport);
		if (reason == null)
			return;

		if (transport == TdcpTransport.Relay && operation == "preview")
			_log.Warning("Relay file.preview refused: {Reason} (node {NodeId}, {Path})", reason, nodeId, fullPath);
		else if (transport == TdcpTransport.Relay)
			_log.Warning("Relay file.read refused: {Reason} (node {NodeId}, {Path})", reason, nodeId, fullPath);

		throw new TdcpException(Constants.FileAccess.DeniedMessage + " " + reason);
	}

	private string? Check(string fullPath, string nodeId, string workingDirectory, long size, TdcpTransport transport)
	{
		if (FileAccessPolicy.IsDenied(fullPath, _dataRoot) || LinkTargetDenied(fullPath))
			return "It is a credential, key or token file.";

		if (transport != TdcpTransport.Relay)
			return null;

		var settings = _settings.Settings.FileAccess;
		var (kind, _) = FileAccessPolicy.Classify(fullPath);
		var cap = kind switch
		{
			FileTypeClass.Text  => settings.MaxTextFileBytes,
			FileTypeClass.Image => settings.MaxImageFileBytes,
			FileTypeClass.Pdf   => settings.MaxPdfFileBytes,
			FileTypeClass.Video => settings.MaxVideoFileBytes,
			_ => 0,
		};
		if (kind == FileTypeClass.Other)
			return "Its type is not on the allowlist (md, txt, code, json, images, video, pdf).";
		if (size > cap)
			return $"It is larger than the {cap} byte limit for its type.";

		if (IsReferenced(nodeId, fullPath))
			return null;

		if (settings.Mode == FileAccessMode.WorkingDirectory && IsUnder(workingDirectory, fullPath))
			return null;

		return "Only files a Focus item references can be read remotely.";
	}

	private bool IsReferenced(string nodeId, string fullPath) =>
		_attention.GetItems(nodeId).Any(i => i.Refs.Any(r =>
			r.Kind == Constants.Attention.RefKindFile && string.Equals(r.AbsolutePath, fullPath, StringComparison.OrdinalIgnoreCase)));

	private static bool IsUnder(string directory, string fullPath)
	{
		if (string.IsNullOrWhiteSpace(directory))
			return false;

		var root = Path.GetFullPath(directory).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
		return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>A file link whose target is a denied path.</summary>
	private bool LinkTargetDenied(string fullPath)
	{
		try
		{
			var target = new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true);
			return target != null && FileAccessPolicy.IsDenied(target.FullName, _dataRoot);
		}
		catch (IOException)
		{
			return true;
		}
	}

	private void PurgeExpired()
	{
		if (_previews.Count < Constants.FileAccess.MaxPreviewHandles)
			return;

		var now = _time.GetUtcNow();
		foreach (var pair in _previews.Where(p => p.Value.ExpiresUtc <= now).ToList())
			_previews.TryRemove(pair.Key, out _);

		// Still full: drop the oldest.
		while (_previews.Count >= Constants.FileAccess.MaxPreviewHandles)
		{
			var oldest = _previews.MinBy(p => p.Value.ExpiresUtc);
			if (oldest.Key == null || !_previews.TryRemove(oldest.Key, out _))
				break;
		}
	}

	// ── chunk reading ────────────────────────────────────────────────────────

	/// <summary>
	/// Reads up to <paramref name="want"/> bytes at <paramref name="start"/>. For text the chunk is moved onto UTF-8 character
	/// boundaries (forward at the start, back at the end unless it reaches the end of the file), so the next chunk begins at
	/// <c>offset + length</c> and no character is cut.
	/// </summary>
	private static (string Data, long Offset, int Count) ReadChunk(string path, long start, int want, long fileLength, bool text)
	{
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		var extra  = text ? MaxUtf8SequenceBytes : 0;
		var buffer = new byte[(int)Math.Min((long)want + extra, Math.Max(0, fileLength - start))];
		stream.Seek(start, SeekOrigin.Begin);
		var read = 0;
		while (read < buffer.Length)
		{
			var n = stream.Read(buffer, read, buffer.Length - read);
			if (n == 0)
				break;
			read += n;
		}

		if (!text)
			return (Convert.ToBase64String(buffer, 0, Math.Min(read, want)), start, Math.Min(read, want));

		var skip = 0;
		while (start > 0 && skip < read && skip < MaxUtf8SequenceBytes - 1 && (buffer[skip] & 0xC0) == 0x80)
			skip++;

		var end = Math.Min(read, skip + want);
		if (start + end < fileLength && end > skip)
		{
			var cut = TrimPartialCharacter(buffer, skip, end);
			// A chunk smaller than its first character still returns that whole character, so a client always makes progress.
			end = cut > skip ? cut : Math.Min(read, skip + CharacterLength(buffer[skip]));
		}

		return (Encoding.UTF8.GetString(buffer, skip, end - skip), start + skip, end - skip);
	}

	/// <summary>The end index that drops a UTF-8 character cut off at <paramref name="end"/> (unchanged when the last one is complete).</summary>
	private static int TrimPartialCharacter(byte[] buffer, int from, int end)
	{
		var i = end - 1;
		var back = 0;
		while (i > from && (buffer[i] & 0xC0) == 0x80 && back < MaxUtf8SequenceBytes - 1)
		{
			i--;
			back++;
		}

		return end - i < CharacterLength(buffer[i]) ? i : end;
	}

	private static int CharacterLength(byte lead) => lead >= 0xF0 ? 4 : lead >= 0xE0 ? 3 : lead >= 0xC0 ? 2 : 1;
}
