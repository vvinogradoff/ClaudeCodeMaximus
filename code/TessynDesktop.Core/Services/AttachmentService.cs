using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IAttachmentService" />
/// <remarks>
/// The profile folder is the session's resolved profile id (<c>default</c> for the Default profile), so each Claude account keeps its own
/// attachments. The attachment id is stateless (it encodes profile, session and file name), so it survives restarts and needs no index.
/// Created by Claude
/// </remarks>
public sealed class AttachmentService : IAttachmentService
{
	private readonly ILogger _log;
	private readonly ISessionRegistry _registry;
	private readonly IAppSettingsService _settings;
	private readonly Func<SessionNodeModel, string?> _profileName;
	private readonly IRelayBlobClient? _blobs;
	private readonly string _dataRoot;
	private readonly string[] _dataRoots;
	private readonly string[] _userProfiles;
	private readonly TimeProvider _time;

	public AttachmentService(
		ISessionRegistry registry,
		IAppSettingsService settings,
		Lazy<ISessionTurnService> turns,
		IRelayBlobClient? blobs = null)
		: this(registry, settings, node => turns.Value.ResolveProfileName(node), blobs)
	{
	}

	/// <summary>For tests and hosts that resolve the profile display name themselves.</summary>
	public AttachmentService(
		ISessionRegistry registry,
		IAppSettingsService settings,
		Func<SessionNodeModel, string?> profileName,
		IRelayBlobClient? blobs = null,
		string? dataRoot = null,
		TimeProvider? time = null,
		ILogger? log = null,
		string? userProfile = null)
	{
		_registry     = registry;
		_settings     = settings;
		_profileName  = profileName;
		_blobs        = blobs;
		_dataRoot     = dataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Constants.AppDataFolderName);
		_dataRoots    = FileAccessPolicy.WithCanonical(_dataRoot);
		_userProfiles = FileAccessPolicy.WithCanonical(userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
		_time         = time ?? TimeProvider.System;
		_log          = log ?? Log.ForContext<AttachmentService>();
	}

	private string Root => Path.Combine(_dataRoot, Constants.Attachments.FolderName);

	// ── attachment.import ────────────────────────────────────────────────────

	/// <inheritdoc />
	public AttachmentDto Import(string nodeId, string sourcePath)
	{
		var node = RemoteSessionLookup.Require(_registry, nodeId);
		if (!SafeFilePath.TryNormalize(sourcePath, node.WorkingDirectory, out var full, out var reason))
			throw new TdcpInvalidParamsException($"'sourcePath' is not usable: {reason}");

		if (SafeFilePath.HasNonLocalLink(full))
			throw new TdcpException("The file is on a network location");

		using var opened = SafeFilePath.Open(full) ?? throw new TdcpException("File not found");

		// The same deny list as a loopback file.read: import must not copy credentials to where the relay can read them.
		if (opened.FinalPath.Length == 0
		    || FileAccessPolicy.IsDeniedOpened(full, opened.FinalPath, opened.LinkCount, referenced: false, _dataRoots, _userProfiles, relay: false))
			throw new TdcpException(Constants.FileAccess.DeniedMessage + " It is a credential, key or token file.");

		var folder = EnsureFolder(node);
		var temp   = TempPath(folder);
		try
		{
			using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				opened.Stream.CopyTo(target, Constants.Attachments.CopyBufferBytes);

			var stored = Store(folder, temp, Path.GetFileName(full));
			_log.Information("Attachment imported: {Name} ({Size} bytes) for node {NodeId}", stored.Name, stored.Size, node.NodeId);
			return stored;
		}
		finally
		{
			TryDelete(temp);
		}
	}

	// ── attachment.fetch ─────────────────────────────────────────────────────

	/// <inheritdoc />
	public async Task<AttachmentDto> FetchAsync(string nodeId, string blobId, string name, long size, string sha256, CancellationToken cancellationToken)
	{
		var node = RemoteSessionLookup.Require(_registry, nodeId);
		if (size < 0 || size > Constants.Attachments.MaxFetchBytes)
			throw new TdcpException($"The file is larger than the {Constants.Attachments.MaxFetchBytes / (1024 * 1024)} MB limit");
		if (sha256.Length != Constants.Attachments.Sha256HexLength || !sha256.All(Uri.IsHexDigit))
			throw new TdcpInvalidParamsException("'sha256' must be 64 hex characters");
		if (_blobs == null)
			throw new TdcpException(Constants.Attachments.NotConnectedMessage);

		var folder = EnsureFolder(node);
		var temp   = TempPath(folder);
		try
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(Constants.Attachments.FetchTimeout);

			string actualHash;
			long written;
			using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			using (var limited = new LimitedHashingStream(target, size))
			{
				try
				{
					await _blobs.DownloadBlobAsync(blobId, limited, timeout.Token).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
				{
					throw new TdcpException("The download from the web server timed out");
				}

				written    = limited.Written;
				actualHash = limited.Hash();
			}

			if (written != size)
				throw new TdcpException($"The download is incomplete or the wrong size ({written} of {size} bytes)");
			if (!string.Equals(actualHash, sha256, StringComparison.OrdinalIgnoreCase))
				throw new TdcpException("The downloaded file does not match its SHA-256");

			var stored = Store(folder, temp, name);
			_log.Information("Attachment fetched: {Name} ({Size} bytes) for node {NodeId}", stored.Name, stored.Size, node.NodeId);
			return stored;
		}
		finally
		{
			TryDelete(temp);
		}
	}

	// ── attachment.list ──────────────────────────────────────────────────────

	/// <inheritdoc />
	public AttachmentListResult List(string nodeId)
	{
		var node = RemoteSessionLookup.Require(_registry, nodeId);
		var safeNode = SafeSegment(node.NodeId);
		var items = new List<AttachmentDto>();
		if (Directory.Exists(Root))
		{
			foreach (var profileDir in Directory.EnumerateDirectories(Root))
			{
				var nodeDir = Path.Combine(profileDir, safeNode);
				if (!Directory.Exists(nodeDir))
					continue;

				foreach (var file in Directory.EnumerateFiles(nodeDir))
				{
					if (!file.EndsWith(Constants.Attachments.TempExtension, StringComparison.OrdinalIgnoreCase))
						items.Add(Describe(Path.GetFileName(profileDir), safeNode, new FileInfo(file)));
				}
			}
		}

		return new AttachmentListResult(node.NodeId, [.. items.OrderByDescending(i => i.CreatedUtc).ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase)]);
	}

	// ── handles ──────────────────────────────────────────────────────────────

	/// <inheritdoc />
	public bool TryResolve(string attachmentId, out string nodeId, out string fullPath)
	{
		nodeId = fullPath = string.Empty;
		if (!attachmentId.StartsWith(Constants.Attachments.IdPrefix, StringComparison.Ordinal))
			return false;

		string[] parts;
		try
		{
			var bytes = FromBase64Url(attachmentId[Constants.Attachments.IdPrefix.Length..]);
			parts = Encoding.UTF8.GetString(bytes).Split('/');
		}
		catch (FormatException)
		{
			return false;
		}

		// Three plain names, nothing that could climb out of the attachments folder.
		if (parts.Length != 3 || parts.Any(p => p.Length == 0 || p != SafeSegment(p) || p is "." or ".."))
			return false;

		var path = Path.Combine(Root, parts[0], parts[1], parts[2]);
		if (!File.Exists(path) || path.EndsWith(Constants.Attachments.TempExtension, StringComparison.OrdinalIgnoreCase))
			return false;

		nodeId   = parts[1];
		fullPath = path;
		return true;
	}

	private static string MakeId(string profileKey, string nodeSegment, string fileName) =>
		Constants.Attachments.IdPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{profileKey}/{nodeSegment}/{fileName}"))
			.TrimEnd('=').Replace('+', '-').Replace('/', '_');

	private static byte[] FromBase64Url(string text)
	{
		var padded = text.Replace('-', '+').Replace('_', '/');
		padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
		return Convert.FromBase64String(padded);
	}

	// ── storage ──────────────────────────────────────────────────────────────

	private string ProfileKey(SessionNodeModel node)
	{
		var display = _profileName(node);
		if (string.IsNullOrEmpty(display))
			return Constants.Attachments.DefaultProfileKey;

		lock (_settings.SyncRoot)
		{
			var profile = _settings.Settings.Profiles.FirstOrDefault(p => string.Equals(p.DisplayName, display, StringComparison.Ordinal));
			return profile == null ? Constants.Attachments.DefaultProfileKey : SafeSegment(profile.ProfileId);
		}
	}

	private string EnsureFolder(SessionNodeModel node)
	{
		var folder = Path.Combine(Root, ProfileKey(node), SafeSegment(node.NodeId));
		Directory.CreateDirectory(folder);
		return folder;
	}

	private static string TempPath(string folder) =>
		Path.Combine(folder, Guid.NewGuid().ToString("N") + Constants.Attachments.TempExtension);

	/// <summary>Moves the finished temp file to <c>yyyyMMdd-HHmmss-name</c>; when that name exists, <c>-2</c>, <c>-3</c>, ... goes before the extension.</summary>
	private AttachmentDto Store(string folder, string tempPath, string requestedName)
	{
		var safe      = SafeFileName(requestedName);
		var stamp     = _time.GetUtcNow().ToString(Constants.Attachments.TimestampFormat);
		var extension = Path.GetExtension(safe);
		var stem      = Path.GetFileNameWithoutExtension(safe);

		for (var suffix = 1; suffix <= Constants.Attachments.MaxSuffix; suffix++)
		{
			var fileName = suffix == 1 ? $"{stamp}-{safe}" : $"{stamp}-{stem}-{suffix}{extension}";
			var target   = Path.Combine(folder, fileName);
			try
			{
				File.Move(tempPath, target, overwrite: false);
			}
			catch (IOException) when (File.Exists(target))
			{
				continue;
			}

			return Describe(Path.GetFileName(Path.GetDirectoryName(folder)!), Path.GetFileName(folder), new FileInfo(target));
		}

		throw new TdcpException("Could not find a free name for the attachment");
	}

	private static AttachmentDto Describe(string profileKey, string nodeSegment, FileInfo file)
	{
		var name = file.Name.Length > Constants.Attachments.PrefixLength ? file.Name[Constants.Attachments.PrefixLength..] : file.Name;
		var (_, mime) = FileAccessPolicy.Classify(file.FullName);
		return new AttachmentDto(
			MakeId(profileKey, nodeSegment, file.Name),
			file.FullName, name, file.Length, mime,
			new DateTimeOffset(file.CreationTimeUtc, TimeSpan.Zero));
	}

	/// <summary>A name that is safe on Windows: no path parts, no invalid or control characters, no reserved device names, bounded length.</summary>
	public static string SafeFileName(string? requested)
	{
		// Not Path.GetFileName: it would read "a:b.txt" as a drive and drop "a:".
		var text = requested ?? string.Empty;
		var name = text[(text.LastIndexOfAny(['/', '\\']) + 1)..];
		var invalid = Path.GetInvalidFileNameChars();
		var cleaned = new string([.. name.Select(c => c < ' ' || invalid.Contains(c) ? Constants.Attachments.FallbackNameChar : c)]).Trim().TrimEnd('.', ' ');
		if (cleaned.Length == 0 || cleaned.All(c => c == Constants.Attachments.FallbackNameChar))
			cleaned = Constants.Attachments.FallbackName;

		var extension = Path.GetExtension(cleaned);
		var stem      = Path.GetFileNameWithoutExtension(cleaned);
		if (stem.Length == 0)
		{
			stem      = cleaned;
			extension = string.Empty;
		}

		if (IsReservedDeviceName(stem))
			stem = Constants.Attachments.FallbackNameChar + stem;
		if (stem.Length + extension.Length > Constants.Attachments.MaxNameLength)
			stem = stem[..Math.Max(1, Constants.Attachments.MaxNameLength - extension.Length)];

		return stem + extension;
	}

	private static bool IsReservedDeviceName(string stem)
	{
		var upper = stem.ToUpperInvariant();
		return upper is "CON" or "PRN" or "AUX" or "NUL"
		       || (upper.Length == 4 && (upper.StartsWith("COM", StringComparison.Ordinal) || upper.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(upper[3]));
	}

	/// <summary>A folder name made from an id: characters that are not safe in a path are replaced.</summary>
	private static string SafeSegment(string value)
	{
		var invalid = Path.GetInvalidFileNameChars();
		return new string([.. value.Select(c => c < ' ' || invalid.Contains(c) || c == '/' || c == '\\' ? Constants.Attachments.FallbackNameChar : c)]);
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
				File.Delete(path);
		}
		catch (IOException)
		{
			// A leftover .part file is invisible to the list and harmless.
		}
	}

	/// <summary>Write-through stream that hashes what passes and refuses more than the expected size, so an oversized blob stops early.</summary>
	private sealed class LimitedHashingStream : Stream
	{
		private readonly Stream _inner;
		private readonly long _limit;
		private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

		public LimitedHashingStream(Stream inner, long limit)
		{
			_inner = inner;
			_limit = limit;
		}

		public long Written { get; private set; }

		public string Hash() => Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();

		public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			Check(buffer.Length);
			_hash.AppendData(buffer.Span);
			await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
		}

		public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
			WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

		public override void Write(byte[] buffer, int offset, int count)
		{
			Check(count);
			_hash.AppendData(buffer, offset, count);
			_inner.Write(buffer, offset, count);
		}

		private void Check(int count)
		{
			Written += count;
			if (Written > _limit)
				throw new TdcpException("The download is larger than announced");
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				_hash.Dispose();
			base.Dispose(disposing);
		}

		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() => _inner.Flush();
		public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
	}
}
