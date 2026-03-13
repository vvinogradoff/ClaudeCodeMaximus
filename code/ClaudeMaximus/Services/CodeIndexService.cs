using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClaudeMaximus.Models;
using Serilog;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public sealed class CodeIndexService : ICodeIndexService
{
	private static readonly ILogger _log = Log.ForContext<CodeIndexService>();

	private readonly ConcurrentDictionary<string, CodeIndex> _indexes = new(StringComparer.OrdinalIgnoreCase);

	public async Task GetOrCreateIndexAsync(string workingDirectory)
	{
		if (string.IsNullOrWhiteSpace(workingDirectory)) return;

		var normalized = NormalizePath(workingDirectory);

		if (_indexes.TryGetValue(normalized, out var existing))
		{
			Interlocked.Increment(ref existing.RefCount);
			return;
		}

		var index = new CodeIndex(normalized);
		if (_indexes.TryAdd(normalized, index))
		{
			Interlocked.Increment(ref index.RefCount);
			try
			{
				await index.BuildAsync();
			}
			catch (Exception ex)
			{
				_log.Error(ex, "Failed to build code index for {Directory}", normalized);
				_indexes.TryRemove(normalized, out _);
				index.Dispose();
			}
		}
		else
		{
			// Another thread added it first
			index.Dispose();
			if (_indexes.TryGetValue(normalized, out existing))
				Interlocked.Increment(ref existing.RefCount);
		}
	}

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
