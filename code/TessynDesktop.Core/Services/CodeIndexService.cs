using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Serilog;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class CodeIndexService : ICodeIndexService
{
	private static readonly ILogger _log = Log.ForContext<CodeIndexService>();

	private readonly ConcurrentDictionary<string, CodeIndex> _indexes = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Test seam: creates the index for a directory.</summary>
	internal Func<string, CodeIndex> IndexFactory { get; set; } = static directory => new CodeIndex(directory);

	public async Task<bool> GetOrCreateIndexAsync(string workingDirectory)
	{
		if (string.IsNullOrWhiteSpace(workingDirectory)) return false;

		var normalized = NormalizePath(workingDirectory);

		if (_indexes.TryGetValue(normalized, out var existing))
		{
			Interlocked.Increment(ref existing.RefCount);
			return true;
		}

		var index = IndexFactory(normalized);
		if (_indexes.TryAdd(normalized, index))
		{
			Interlocked.Increment(ref index.RefCount);
			try
			{
				await index.BuildAsync();
				return true;
			}
			catch (OperationCanceledException)
			{
				// Released while the first scan was still running; ReleaseIndex already removed and disposed it.
				// The caller's reference went with that release, so nothing is held.
				RemoveIfSame(normalized, index);
				return false;
			}
			catch (Exception ex)
			{
				// A failed build holds no reference: the caller must not release one it never got.
				_log.Error(ex, "Failed to build code index for {Directory}", normalized);
				RemoveIfSame(normalized, index);
				index.Dispose();
				return false;
			}
		}

		// Another thread added it first
		index.Dispose();
		if (_indexes.TryGetValue(normalized, out existing))
		{
			Interlocked.Increment(ref existing.RefCount);
			return true;
		}

		return false;
	}

	/// <summary>Removes the entry only if it is still this instance, so a newer index for the same directory survives.</summary>
	private void RemoveIfSame(string normalized, CodeIndex index) =>
		_indexes.TryRemove(new KeyValuePair<string, CodeIndex>(normalized, index));

	public void ReleaseIndex(string workingDirectory)
	{
		if (string.IsNullOrWhiteSpace(workingDirectory)) return;

		var normalized = NormalizePath(workingDirectory);
		if (!_indexes.TryGetValue(normalized, out var index)) return;

		var newCount = Interlocked.Decrement(ref index.RefCount);
		if (newCount <= 0)
		{
			if (_indexes.TryRemove(normalized, out var removed))
			{
				removed.Dispose();
			}
		}
	}

	public IReadOnlyList<IndexedFileModel> SearchFiles(string workingDirectory, string query, int maxResults = 15)
	{
		if (string.IsNullOrWhiteSpace(query)) return Array.Empty<IndexedFileModel>();

		var normalized = NormalizePath(workingDirectory);
		if (!_indexes.TryGetValue(normalized, out var index)) return Array.Empty<IndexedFileModel>();

		var snapshot = index.FileSnapshot;
		return TieredSearch(snapshot, query, f => f.FileName, maxResults);
	}

	public IReadOnlyList<CodeSymbolModel> SearchSymbols(string workingDirectory, string query, int maxResults = 15)
	{
		if (string.IsNullOrWhiteSpace(query)) return Array.Empty<CodeSymbolModel>();

		var normalized = NormalizePath(workingDirectory);
		if (!_indexes.TryGetValue(normalized, out var index)) return Array.Empty<CodeSymbolModel>();

		var snapshot = index.SymbolSnapshot;
		return TieredSearch(snapshot, query, s => s.Name, maxResults);
	}

	public bool IsIndexReady(string workingDirectory)
	{
		if (string.IsNullOrWhiteSpace(workingDirectory)) return false;
		var normalized = NormalizePath(workingDirectory);
		return _indexes.TryGetValue(normalized, out var index) && index.IsReady;
	}

	private static IReadOnlyList<T> TieredSearch<T>(T[] items, string query, Func<T, string> nameSelector, int maxResults)
	{
		var tier1 = new List<T>();
		var tier2 = new List<T>();
		var tier3 = new List<T>();
		var tier4 = new List<T>();

		foreach (var item in items)
		{
			var name = nameSelector(item);

			if (name.StartsWith(query, StringComparison.Ordinal))
			{
				tier1.Add(item);
				// Tier 1 is the best tier: once it alone fills the result there is nothing to gain from scanning on.
				if (tier1.Count >= maxResults) return tier1;
			}
			else if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
			{
				tier2.Add(item);
			}
			else if (name.Contains(query, StringComparison.Ordinal))
			{
				tier3.Add(item);
			}
			else if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
			{
				tier4.Add(item);
			}
		}

		var result = new List<T>(Math.Min(maxResults, tier1.Count + tier2.Count + tier3.Count + tier4.Count));
		foreach (var tier in new[] { tier1, tier2, tier3, tier4 })
		{
			foreach (var item in tier)
			{
				if (result.Count >= maxResults) return result;
				result.Add(item);
			}
		}

		return result;
	}

	private static string NormalizePath(string path)
	{
		return path.TrimEnd('\\', '/');
	}
}
