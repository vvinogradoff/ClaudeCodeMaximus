using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Tessyn.Web.Options;

namespace Tessyn.Web.Link;

/// <summary>A blob waiting for the daemon: its id, file path, size and SHA-256 (lower-case hex).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record StoredBlob(string Id, string Path, long Size, string Sha256);

/// <summary>Thrown by <see cref="BlobStore.SaveAsync"/> when the blob folder has no room: its byte quota is used up or the disk is full.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class BlobStorageFullException : Exception
{
	public BlobStorageFullException(string message, Exception? inner = null) : base(message, inner)
	{
	}
}

/// <summary>Thrown by <see cref="BlobStore.SaveAsync"/> when the stream is larger than the limit.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class BlobTooLargeException : Exception
{
	public BlobTooLargeException(long limit) : base($"The upload is larger than {limit} bytes")
	{
	}
}

/// <summary>
/// Temporary store for browser uploads: streamed to disk with a SHA-256 on the way in, fetched once by the daemon over the link,
/// then deleted. Nothing is kept in memory, and a blob that nobody fetched is swept after its time to live.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class BlobStore
{
	private string _directory;
	private readonly TimeProvider _time;
	private readonly TimeSpan _ttl;
	private readonly ILogger<BlobStore> _logger;
	private readonly long _quota;
	private readonly object _gate = new();
	private readonly HashSet<string> _claimed = new(StringComparer.Ordinal);
	// Claimed blobs whose delete keeps failing: nobody can claim them again, and every sweep retries the delete.
	private readonly HashSet<string> _abandoned = new(StringComparer.Ordinal);
	private readonly Dictionary<string, int> _deleteFailures = new(StringComparer.Ordinal);
	private const int MaxDeleteFailures = 3;

	public BlobStore(IOptions<TessynWebOptions> options, TimeProvider time, ILogger<BlobStore> logger)
	{
		var configured = options.Value.Attachments;
		_directory = string.IsNullOrWhiteSpace(configured.BlobDirectory)
			? System.IO.Path.Combine(System.IO.Path.GetTempPath(), Constants.Defaults.BlobFolderName)
			: configured.BlobDirectory;
		_time = time;
		_ttl = TimeSpan.FromMinutes(configured.BlobTtlMinutes);
		_quota = configured.MaxBlobDirectoryBytes;
		_logger = logger;
		CreatePrivateDirectory();
		Sweep();
	}

	/// <summary>Uploads wait here for the daemon: on Unix the folder is for this user only (0700), the files 0600. Windows ACLs are left as they are.</summary>
	private void CreatePrivateDirectory()
	{
		if (OperatingSystem.IsWindows())
		{
			Directory.CreateDirectory(_directory);
			return;
		}

		// Only a folder this process creates is chmod-ed. One that already exists is used only when it is private (0700) and writable
		// for us; otherwise (someone else's, or too open) the store works in a private folder of its own and says so, never touching theirs.
		const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
		try
		{
			if (!Directory.Exists(_directory))
			{
				Directory.CreateDirectory(_directory, Private);
				File.SetUnixFileMode(_directory, Private);
				return;
			}

			if (File.GetUnixFileMode(_directory) == Private && CanWriteHere(_directory))
				return;
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
		{
			_logger.LogWarning(ex, "The blob folder {Directory} cannot be used", _directory);
		}

		var own = Directory.CreateTempSubdirectory(Constants.Defaults.BlobFolderName + "-").FullName;
		_logger.LogWarning("The blob folder {Directory} is not private to this user; uploads wait in {Own} instead", _directory, own);
		_directory = own;
	}

	private static bool CanWriteHere(string directory)
	{
		var probe = System.IO.Path.Combine(directory, Guid.NewGuid().ToString("N") + ".probe");
		try
		{
			using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write))
			{
			}

			File.Delete(probe);
			return true;
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
		{
			return false;
		}
	}

	/// <summary>Bytes the blobs in the folder hold now.</summary>
	public long UsedBytes()
	{
		long total = 0;
		foreach (var file in Directory.EnumerateFiles(_directory, "*" + Constants.Defaults.BlobExtension))
		{
			try
			{
				total += new FileInfo(file).Length;
			}
			catch (IOException)
			{
				// Deleted while counting.
			}
		}

		return total;
	}

	/// <summary>True for the IOException a full disk throws (Windows ERROR_DISK_FULL / ERROR_HANDLE_DISK_FULL, Unix ENOSPC).</summary>
	public static bool IsDiskFull(IOException ex)
	{
		var code = ex.HResult & Constants.Limits.HResultCodeMask;
		return code is Constants.Limits.DiskFullWin or Constants.Limits.HandleDiskFullWin || ex.HResult == Constants.Limits.DiskFullUnix;
	}

	/// <summary>Streams <paramref name="source"/> into a new blob; throws <see cref="BlobTooLargeException"/> beyond <paramref name="maxBytes"/>.</summary>
	public async Task<StoredBlob> SaveAsync(Stream source, long maxBytes, CancellationToken cancellationToken)
	{
		Sweep();
		var used = UsedBytes();
		if (used >= _quota)
			throw new BlobStorageFullException("The blob folder is full");

		var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(Constants.Limits.BlobIdHexLength / 2)).ToLowerInvariant();
		var path = PathFor(id);
		try
		{
			using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			long total = 0;
			var options = new FileStreamOptions
			{
				Mode = FileMode.CreateNew,
				Access = FileAccess.Write,
				Share = FileShare.None,
				BufferSize = Constants.Limits.CopyBufferBytes,
				Options = FileOptions.Asynchronous,
			};
			if (!OperatingSystem.IsWindows())
				options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

			await using (var target = new FileStream(path, options))
			{
				var buffer = new byte[Constants.Limits.CopyBufferBytes];
				int read;
				while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
				{
					total += read;
					if (total > maxBytes)
						throw new BlobTooLargeException(maxBytes);

					if (used + total > _quota)
						throw new BlobStorageFullException("The blob folder is full");

					hash.AppendData(buffer, 0, read);
					await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
				}
			}

			return new StoredBlob(id, path, total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
		}
		catch (IOException ex) when (IsDiskFull(ex))
		{
			TryDelete(path);
			throw new BlobStorageFullException("The disk is full", ex);
		}
		catch
		{
			TryDelete(path);
			throw;
		}
	}

	/// <summary>Claims a blob for download: true once per blob. An unknown or malformed id, or one that was already claimed, gives false.</summary>
	public bool TryClaim(string id, out StoredBlob blob)
	{
		blob = null!;
		if (!IsBlobId(id))
			return false;

		var path = PathFor(id);
		lock (_gate)
		{
			if (_abandoned.Contains(id) || !File.Exists(path) || !_claimed.Add(id))
				return false;
		}

		blob = new StoredBlob(id, path, new FileInfo(path).Length, string.Empty);
		return true;
	}

	/// <summary>Deletes the blob (idempotent).</summary>
	public void Delete(string id)
	{
		if (!IsBlobId(id))
			return;

		// The file goes first: while it is still there (a download holds it open on Windows) the claim stays, so nobody can claim it again.
		var path = PathFor(id);
		TryDelete(path);
		lock (_gate)
		{
			if (!File.Exists(path))
			{
				_claimed.Remove(id);
				_deleteFailures.Remove(id);
			}
			else if (_claimed.Contains(id))
			{
				// A delete that keeps failing must not hold the claim, and its bytes, for good: after a few tries the sweep takes over.
				_deleteFailures[id] = _deleteFailures.GetValueOrDefault(id) + 1;
				if (_deleteFailures[id] >= MaxDeleteFailures)
					Abandon(id, $"its delete failed {MaxDeleteFailures} times");
			}
		}
	}

	/// <summary>Releases the claim of a blob that cannot be deleted; the sweep retries the delete. Call under the gate.</summary>
	private void Abandon(string id, string reason)
	{
		_claimed.Remove(id);
		_deleteFailures.Remove(id);
		if (_abandoned.Add(id))
			_logger.LogWarning("Blob {BlobId} is released from its claim ({Reason}); the sweep keeps retrying the delete", id, reason);
	}

	/// <summary>Removes blobs older than the time to live; the failures of one file never stop the rest.</summary>
	public void Sweep()
	{
		var cutoff = _time.GetUtcNow().UtcDateTime - _ttl;
		var claimCutoff = _time.GetUtcNow().UtcDateTime - _ttl * 2;
		try
		{
			foreach (var file in Directory.EnumerateFiles(_directory, "*" + Constants.Defaults.BlobExtension))
			{
				var id = System.IO.Path.GetFileNameWithoutExtension(file);
				var written = File.GetLastWriteTimeUtc(file);
				bool abandoned, inFlight;
				lock (_gate)
				{
					abandoned = _abandoned.Contains(id);
					inFlight = _claimed.Contains(id);
					// A claimed blob whose delete already failed and that is still here after twice the time to live is stuck, not in flight:
					// release it, then delete it. (A claim without a failed delete is a download, however long it takes.)
					if (inFlight && written < claimCutoff && _deleteFailures.ContainsKey(id))
					{
						Abandon(id, "its delete failed and it is still claimed after twice the time to live");
						abandoned = true;
						inFlight = false;
					}
				}

				if (!abandoned && (written >= cutoff || inFlight))
					continue;   // young, or a blob the daemon is downloading right now

				TryDelete(file);
				if (abandoned && !File.Exists(file))
				{
					lock (_gate)
						_abandoned.Remove(id);
				}
			}
		}
		catch (IOException ex)
		{
			_logger.LogWarning(ex, "Could not sweep the blob folder");
		}
	}

	private string PathFor(string id) => System.IO.Path.Combine(_directory, id + Constants.Defaults.BlobExtension);

	private static bool IsBlobId(string id) =>
		id.Length == Constants.Limits.BlobIdHexLength && id.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

	private void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_logger.LogWarning(ex, "Could not delete a blob");
		}
	}
}
