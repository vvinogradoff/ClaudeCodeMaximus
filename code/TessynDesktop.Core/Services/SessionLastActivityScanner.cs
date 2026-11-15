using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Reads the last user prompt, last assistant response, prompt profile and draft timestamp for session files.
/// Pure file work with no UI state, so callers run it on a background thread (ISSUE.016).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionLastActivityScanner : ISessionLastActivityScanner
{
	/// <summary>
	/// Files read at the same time by <see cref="ScanNewestFirst"/>. A cold start is bound by per-file open latency
	/// (about 18 ms per file measured, 3.4k files, ~20 KB each), not by bytes, so a few parallel readers overlap that wait.
	/// </summary>
	private const int ReadParallelism = 4;
	private const int BatchSize = ReadParallelism * 4;

	private readonly ISessionFileService _sessionFiles;
	private readonly IDraftService _drafts;

	public SessionLastActivityScanner(ISessionFileService sessionFiles, IDraftService drafts)
	{
		_sessionFiles = sessionFiles;
		_drafts = drafts;
	}

	public IReadOnlyList<SessionLastActivityModel> Scan(IReadOnlyList<string> sessionFileNames)
	{
		var results = new List<SessionLastActivityModel>(sessionFileNames.Count);
		foreach (var fileName in sessionFileNames)
			results.Add(ScanOne(fileName));
		return results;
	}

	public void ScanNewestFirst(
		IReadOnlyList<string> sessionFileNames,
		int maxChunkFiles,
		TimeSpan maxChunkInterval,
		Action<IReadOnlyList<SessionLastActivityModel>> onChunk,
		CancellationToken cancellationToken = default)
	{
		maxChunkFiles = Math.Max(1, maxChunkFiles);

		var ordered = OrderNewestFirst(sessionFileNames);
		var chunk = new List<SessionLastActivityModel>(maxChunkFiles);
		var sinceLastChunk = Stopwatch.StartNew();
		var options = new ParallelOptions { MaxDegreeOfParallelism = ReadParallelism, CancellationToken = cancellationToken };

		for (var start = 0; start < ordered.Count; start += BatchSize)
		{
			if (cancellationToken.IsCancellationRequested)
				return;

			// Read a few files at once, but hand them over in order so the newest stay first.
			var batch = new SessionLastActivityModel[Math.Min(BatchSize, ordered.Count - start)];
			try
			{
				Parallel.For(0, batch.Length, options, i => batch[i] = ScanOne(ordered[start + i]));
			}
			catch (OperationCanceledException)
			{
				return;
			}

			foreach (var result in batch)
			{
				if (cancellationToken.IsCancellationRequested)
					return;

				chunk.Add(result);
				if (chunk.Count >= maxChunkFiles || sinceLastChunk.Elapsed >= maxChunkInterval)
				{
					onChunk(chunk);
					chunk = new List<SessionLastActivityModel>(maxChunkFiles);
					sinceLastChunk.Restart();
				}
			}
		}

		if (chunk.Count > 0 && !cancellationToken.IsCancellationRequested)
			onChunk(chunk);
	}

	/// <summary>Stable order by last-write time, newest first; a missing or unstattable file sorts last.</summary>
	private List<string> OrderNewestFirst(IReadOnlyList<string> fileNames)
	{
		var stamped = new List<(string FileName, DateTimeOffset? WriteTime)>(fileNames.Count);
		foreach (var fileName in fileNames)
		{
			DateTimeOffset? writeTime = null;
			try { writeTime = _sessionFiles.GetLastWriteTimeUtc(fileName); }
			catch { /* unreadable: sorts last */ }
			stamped.Add((fileName, writeTime));
		}

		// OrderByDescending is stable; null sorts first ascending, so it sorts last descending.
		return stamped.OrderByDescending(s => s.WriteTime).Select(s => s.FileName).ToList();
	}

	private SessionLastActivityModel ScanOne(string fileName)
	{
		SessionEntryModel? lastUserEntry = null;
		DateTimeOffset? lastAssistant = null;

		try
		{
			var entries = _sessionFiles.ReadEntries(fileName);
			for (var i = entries.Count - 1; i >= 0 && (lastUserEntry == null || lastAssistant == null); i--)
			{
				if (lastUserEntry == null && entries[i].Role == Constants.SessionFile.RoleUser)
					lastUserEntry = entries[i];
				else if (lastAssistant == null && entries[i].Role == Constants.SessionFile.RoleAssistant)
					lastAssistant = entries[i].Timestamp;
			}
		}
		catch
		{
			// Session file may not exist or be corrupted; leave timestamps null.
		}

		DateTimeOffset? draftTimestamp = null;
		try
		{
			// A draft file can persist with whitespace-only content (e.g. all text deleted without
			// clearing the draft); that isn't a real pending draft, so its timestamp must not count.
			var draftText = _drafts.LoadDraft(fileName);
			if (!string.IsNullOrWhiteSpace(draftText))
				draftTimestamp = _drafts.GetDraftTimestamp(fileName);
		}
		catch
		{
			// Unreadable draft: treat as no draft.
		}

		return new SessionLastActivityModel(fileName, lastUserEntry?.Timestamp, lastAssistant, lastUserEntry?.ProfileName, draftTimestamp);
	}
}
