using System;
using System.IO;

namespace TessynDesktop.Services;

/// <summary>
/// A hash of sampled bytes of the indexed part of a file (its start, the middle entry's header, the last entry's header and
/// surroundings). An append cannot change those bytes; a rewrite almost always does, which is how a rewrite that kept or grew
/// the file's length is told from an append.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class EntryIndexFingerprint
{
	private const ulong OffsetBasis = 14695981039346656037UL;
	private const ulong Prime = 1099511628211UL;

	public ulong Compute(FileStream stream, EntryIndexRecord[] records, int count, long indexedLength)
	{
		var hash = Sample(stream, OffsetBasis, 0, Constants.ContentStore.FingerprintSampleBytes, indexedLength);

		if (count > 0)
		{
			hash = Sample(stream, hash, records[count / 2].Offset, Constants.ContentStore.FingerprintSampleBytes, indexedLength);

			var last  = records[count - 1].Offset;
			var start = Math.Max(0, last - Constants.ContentStore.FingerprintLastEntryLeadBytes);
			hash = Sample(stream, hash, start, last - start + Constants.ContentStore.FingerprintLastEntryTailBytes, indexedLength);
		}

		return hash;
	}

	/// <summary>Folds the bytes of [<paramref name="start"/>, <paramref name="start"/> + <paramref name="length"/>) within the indexed length into the hash.</summary>
	private static ulong Sample(FileStream stream, ulong hash, long start, long length, long indexedLength)
	{
		var end = Math.Min(indexedLength, start + length);
		if (end <= start)
			return Mix(hash, 0);

		var bytes = new byte[end - start];
		stream.Seek(start, SeekOrigin.Begin);
		var read = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);

		// A short read means the file shrank under us: a different hash than the stored one, so the index is rebuilt.
		hash = Mix(hash, read);
		for (var i = 0; i < read; i++)
			hash = (hash ^ bytes[i]) * Prime;

		return hash;
	}

	private static ulong Mix(ulong hash, long value) => (hash ^ (ulong)value) * Prime;
}
