using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IGlobalSearchService" />
/// <remarks>Created by Claude</remarks>
public sealed class GlobalSearchService : IGlobalSearchService
{
	private static readonly ILogger _log = Log.ForContext<GlobalSearchService>();

	private readonly IClaudeSessionIndexService _index;
	private readonly IRemoteTreeReader _tree;
	private readonly ISessionIndexActivity? _activity;
	private int _reindexRunning;

	public GlobalSearchService(IClaudeSessionIndexService index, IRemoteTreeReader tree, ISessionIndexActivity? activity = null)
	{
		_index    = index;
		_tree     = tree;
		_activity = activity;
	}

	/// <inheritdoc />
	public async Task<SearchGlobalResult> SearchAsync(string query, string? projectSlug, int? limit, int? offset, CancellationToken cancellationToken = default)
	{
		query = query.Trim();
		if (query.Length == 0)
			throw new TdcpInvalidParamsException(Constants.GlobalSearch.QueryRequired);
		if (query.Length > Constants.GlobalSearch.QueryMaxChars)
			throw new TdcpInvalidParamsException(Constants.GlobalSearch.QueryTooLong);
		if (projectSlug != null && (projectSlug.Length is 0 or > Constants.GlobalSearch.SlugMaxChars || projectSlug.IndexOfAny(['/', '\\', ':']) >= 0))
			throw new TdcpInvalidParamsException(Constants.GlobalSearch.BadSlug);

		var take = Math.Clamp(limit ?? Constants.GlobalSearch.DefaultLimit, 1, Constants.GlobalSearch.MaxLimit);
		var skip = Math.Max(0, offset ?? 0);

		var messages = await _index.SearchAsync(query, projectSlug, null, Constants.GlobalSearch.FetchMessages, 0, cancellationToken).ConfigureAwait(false);

		// The messages arrive best first; the first one of a session is its hit.
		var perSession = messages.GroupBy(m => m.ExternalId, StringComparer.Ordinal).Select(g => g.First()).ToList();
		var imported   = ImportedNodes();

		var hits = perSession
			.Skip(skip).Take(take)
			.Select(m => new SearchHitDto(
				m.ExternalId, m.SessionTitle, m.ProjectSlug, Cut(m.Content), -m.Rank,
				imported.GetValueOrDefault(m.ExternalId)))
			.ToList();
		return new SearchGlobalResult(hits, perSession.Count);
	}

	private Dictionary<string, string> ImportedNodes()
	{
		var map = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var session in _tree.Snapshot().AllSessions)
		{
			if (session.ExternalKey != null)
				map.TryAdd(session.ExternalKey, session.NodeId);
		}

		return map;
	}

	private static string Cut(string content)
	{
		var text = content.Trim();
		return text.Length > Constants.GlobalSearch.SnippetMaxChars ? text[..Constants.GlobalSearch.SnippetMaxChars] : text;
	}

	/// <inheritdoc />
	public SearchReindexResult StartReindex()
	{
		// One scan at a time: refused while one started here is still running, and while any other scan (the startup one) is.
		if (Interlocked.CompareExchange(ref _reindexRunning, 1, 0) != 0)
			return new SearchReindexResult(false);

		if ((_activity?.ActiveScans ?? 0) > 0)
		{
			Volatile.Write(ref _reindexRunning, 0);
			return new SearchReindexResult(false);
		}

		_ = Task.Run(async () =>
		{
			try
			{
				await _index.ReindexAsync().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "search.reindex: the scan failed");
			}
			finally
			{
				Volatile.Write(ref _reindexRunning, 0);
			}
		});
		return new SearchReindexResult(true);
	}
}
