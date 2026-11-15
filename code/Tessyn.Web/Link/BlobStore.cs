using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Tessyn.Web.Options;

namespace Tessyn.Web.Link;

/// <summary>A blob waiting for the daemon: its id, file path, size and SHA-256 (lower-case hex).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record StoredBlob(string Id, string Path, long Size, string Sha256);

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
	private readonly string _directory;
	private readonly TimeProvider _time;
	private readonly TimeSpan _ttl;
	private readonly ILogger<BlobStore> _logger;
	private readonly object _gate = new();
	private readonly HashSet<string> _claimed = new(StringComparer.Ordinal);

	public BlobStore(IOptions<TessynWebOptions> options, TimeProvider time, ILogger<BlobStore> logger)
	{
		var configured = options.Value.Attachments;
		_directory = string.IsNullOrWhiteSpace(configured.BlobDirectory)
			? System.IO.Path.Combine(System.IO.Path.GetTempPath(), Constants.Defaults.BlobFolderName)
			: configured.BlobDirectory;
		_time = time;
		_ttl = TimeSpan.FromMinutes(configured.BlobTtlMinutes);
		_logger = logger;
		Directory.CreateDirectory(_directory);
		Sweep();
	}

	/// <summary>Streams <paramref name="source"/> into a new blob; throws <see cref="BlobTooLargeException"/> beyond <paramref name="maxBytes"/>.</summary>
	public async Task<StoredBlob> SaveAsync(Stream source, long maxBytes, CancellationToken cancellationToken)
	{
		Sweep();
		var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(Constants.Limits.BlobIdHexLength / 2)).ToLowerInvariant();
		var path = PathFor(id);
		try
		{
			using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			long total = 0;
			await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, Constants.Limits.CopyBufferBytes, useAsync: true))
			{
				var buffer = new byte[Constants.Limits.CopyBufferBytes];
				int read;
				while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
				{
					total += read;
					if (total > maxBytes)
						throw new BlobTooLargeException(maxBytes);

					hash.AppendData(buffer, 0, read);
					await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
				}
			}

			return new StoredBlob(id, path, total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
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
			if (!File.Exists(path) || !_claimed.Add(id))
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

		lock (_gate)
			_claimed.Remove(id);

		TryDelete(PathFor(id));
	}

	/// <summary>Removes blobs older than the time to live; the failures of one file never stop the rest.</summary>
	public void Sweep()
	{
		var cutoff = _time.GetUtcNow().UtcDateTime - _ttl;
		try
		{
			foreach (var file in Directory.EnumerateFiles(_directory, "*" + Constants.Defaults.BlobExtension))
			{
				if (File.GetLastWriteTimeUtc(file) < cutoff)
					TryDelete(file);
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
		catch (IOException ex)
		{
			_logger.LogWarning(ex, "Could not delete a blob");
		}
	}
}
