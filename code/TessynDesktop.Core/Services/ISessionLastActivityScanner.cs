using System;
using System.Collections.Generic;
using System.Threading;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>Batch reader of per-session last-activity timestamps. Thread-safe; touches no UI state.</summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionLastActivityScanner
{
	/// <summary>Returns one result per input file name, in input order. A missing or unreadable file yields null timestamps.</summary>
	IReadOnlyList<SessionLastActivityModel> Scan(IReadOnlyList<string> sessionFileNames);

	/// <summary>
	/// Scans the files newest first (by last-write time; missing files last) and hands the results to
	/// <paramref name="onChunk"/> on the calling thread as soon as <paramref name="maxChunkFiles"/> results are ready or
	/// <paramref name="maxChunkInterval"/> has passed since the last hand-over, so a caller can show the most recent
	/// sessions first. Every input file is reported exactly once unless <paramref name="cancellationToken"/> stops the scan.
	/// </summary>
	void ScanNewestFirst(
		IReadOnlyList<string> sessionFileNames,
		int maxChunkFiles,
		TimeSpan maxChunkInterval,
		Action<IReadOnlyList<SessionLastActivityModel>> onChunk,
		CancellationToken cancellationToken = default);
}
