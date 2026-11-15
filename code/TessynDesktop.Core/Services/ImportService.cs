using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IImportService" />
/// <remarks>
/// <see cref="ImportSession"/> is the former <c>SessionTreeView.ExecuteImport</c> of the Avalonia app, moved here unchanged in what it does
/// (same name rule, same file, same node) apart from the repaired path of a cross-project session. Created by Claude.
/// </remarks>
public sealed partial class ImportService : IImportService
{
	private static readonly ILogger _log = Log.ForContext<ImportService>();

	private readonly IClaudeSessionImportService _importer;
	private readonly IClaudeSessionIndexService _index;
	private readonly ISessionFileService _files;
	private readonly ISessionRegistry _registry;
	private readonly IAppSettingsService _settings;
	private readonly IReadOnlyList<string>? _projectsDirsOverride;

	/// <summary>Ids whose title is being generated now, so a poll does not start the same work twice.</summary>
	private readonly ConcurrentDictionary<string, byte> _titleWork = new(StringComparer.Ordinal);

	public ImportService(
		IClaudeSessionImportService importer,
		IClaudeSessionIndexService index,
		ISessionFileService files,
		ISessionRegistry registry,
		IAppSettingsService settings,
		IReadOnlyList<string>? projectsDirsOverride = null)
	{
		_importer = importer;
		_index    = index;
		_files    = files;
		_registry = registry;
		_settings = settings;
		_projectsDirsOverride = projectsDirsOverride;
	}

	[GeneratedRegex("^[A-Za-z0-9_-]{1,128}$")]
	private static partial Regex ExternalIdPattern();

	// ── Import of one session (the former ExecuteImport) ─────────────────────────────

	/// <inheritdoc />
	public ImportOutcomeModel ImportSession(DirectoryNodeModel? directory, GroupNodeModel? group, ClaudeSessionSummaryModel summary, string? name = null)
	{
		string? fileName = null;
		try
		{
			// Nothing is created when there is nowhere to put the session (the Avalonia code wrote the session file first and left it behind).
			if (directory == null && group == null)
				return new ImportOutcomeModel(null, ImportFailureKind.NoTarget);

			if (string.IsNullOrEmpty(summary.JsonlPath))
				return new ImportOutcomeModel(null, ImportFailureKind.NoSource); // cannot import without a resolvable JSONL path

			var entries = _importer.ParseJsonlSession(summary.JsonlPath);
			if (entries.Count == 0)
				return new ImportOutcomeModel(null, ImportFailureKind.Empty);

			var sessionName = !string.IsNullOrWhiteSpace(name)
				? name.Trim()
				: summary.GeneratedTitle ?? TruncateForSessionName(summary.FirstUserPrompt) ?? Constants.Import.DefaultSessionName;

			fileName = _files.CreateSessionFile();
			_files.WriteSessionFile(fileName, entries);

			var originalProjectPath = FindOriginalProjectPath(summary, directory?.Path ?? group?.WorkingDirectory ?? string.Empty);

			var node = directory != null
				? _registry.ImportSession(directory, sessionName, fileName, summary.SessionId)
				: _registry.ImportSessionToGroup(group!, sessionName, fileName, summary.SessionId);

			if (originalProjectPath != null)
			{
				// The registry saved before the path was known; the path is saved with the next save in the Avalonia code. Here it is saved at once.
				lock (_settings.SyncRoot)
				{
					node.OriginalProjectPath = originalProjectPath;
					_settings.Save();
				}
			}

			return new ImportOutcomeModel(node, ImportFailureKind.None);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "ImportSession: failed to import session {SessionId}", summary.SessionId);
			DeleteQuietly(fileName);
			return new ImportOutcomeModel(null, ImportFailureKind.Error);
		}
	}

	/// <summary>A session whose JSONL lives under another project than the target's remembers the folder it came from; one of the same project needs none.</summary>
	private string? FindOriginalProjectPath(ClaudeSessionSummaryModel summary, string targetPath)
	{
		var jsonlDirectory = Path.GetDirectoryName(summary.JsonlPath);
		if (jsonlDirectory == null)
			return null;

		var slug       = Path.GetFileName(jsonlDirectory);
		var targetSlug = Constants.ClaudeSessions.BuildProjectSlug(targetPath);
		return string.Equals(slug, targetSlug, StringComparison.Ordinal) ? null : ResolveOriginalProjectPath(slug, summary.JsonlPath);
	}

	private void DeleteQuietly(string? fileName)
	{
		if (fileName == null)
			return;

		try { _files.DeleteSessionFile(fileName); }
		catch (Exception ex) { _log.Debug(ex, "ImportSession: could not remove the session file of a failed import"); }
	}

	private static string? TruncateForSessionName(string? prompt)
	{
		if (string.IsNullOrWhiteSpace(prompt))
			return null;

		var firstLine = prompt.Split('\n')[0].Trim();
		return firstLine.Length > Constants.Import.PromptNameMaxChars
			? firstLine[..(Constants.Import.PromptNameMaxChars - Constants.Import.Ellipsis.Length)] + Constants.Import.Ellipsis
			: firstLine;
	}

	// ── The folder behind a project slug ─────────────────────────────────────────────

	/// <inheritdoc />
	public string ResolveOriginalProjectPath(string projectSlug, string? jsonlPath)
	{
		var recorded = ReadRecordedWorkingDirectory(jsonlPath);
		if (recorded != null && string.Equals(Constants.ClaudeSessions.BuildProjectSlug(recorded), projectSlug, StringComparison.Ordinal))
			return recorded;

		lock (_settings.SyncRoot)
		{
			var known = _registry.Directories.FirstOrDefault(d => string.Equals(Constants.ClaudeSessions.BuildProjectSlug(d.Path), projectSlug, StringComparison.Ordinal));
			if (known != null)
				return known.Path;
		}

		return ReconstructPath(projectSlug);
	}

	/// <summary>The <c>cwd</c> Claude Code wrote into the first lines of the session file; null when the file is unreadable or names none.</summary>
	private static string? ReadRecordedWorkingDirectory(string? jsonlPath)
	{
		if (string.IsNullOrEmpty(jsonlPath) || !File.Exists(jsonlPath))
			return null;

		try
		{
			using var reader = new StreamReader(jsonlPath);
			for (var i = 0; i < Constants.Import.JsonlWorkingDirectoryScanLines && reader.ReadLine() is { } line; i++)
			{
				if (!line.Contains($"\"{Constants.Import.JsonlWorkingDirectoryKey}\"", StringComparison.Ordinal))
					continue;

				using var document = JsonDocument.Parse(line);
				if (document.RootElement.ValueKind == JsonValueKind.Object
					&& document.RootElement.TryGetProperty(Constants.Import.JsonlWorkingDirectoryKey, out var cwd)
					&& cwd.ValueKind == JsonValueKind.String
					&& !string.IsNullOrWhiteSpace(cwd.GetString()))
					return cwd.GetString();
			}
		}
		catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
		{
			_log.Debug(ex, "ResolveOriginalProjectPath: could not read the recorded working directory");
		}

		return null;
	}

	/// <summary>
	/// The last resort: a path that reads like the folder and gives the same slug again. "C--Projects-Personal-Tessyn" is "C:\Projects\Personal\Tessyn",
	/// "-Users-me-app" is "/Users/me/app". A '-' that was part of a folder name cannot be told from a separator, which is why this comes last.
	/// </summary>
	private static string ReconstructPath(string slug)
	{
		if (slug.Length >= 3 && char.IsAsciiLetter(slug[0]) && slug[1] == '-' && slug[2] == '-')
			return slug[0] + @":\" + slug[3..].Replace('-', '\\');

		return slug.StartsWith('-') ? slug.Replace('-', '/') : slug;
	}

	// ── Listing ──────────────────────────────────────────────────────────────────────

	/// <inheritdoc />
	public async Task<ImportListResult> ListAsync(string? directoryId, bool global, string? query, int limit, int offset, CancellationToken cancellationToken = default)
	{
		query = query?.Trim();
		if (query is { Length: > Constants.Import.QueryMaxChars })
			throw new TdcpInvalidParamsException(Constants.Import.QueryTooLong);

		var already = _registry.CollectAllClaudeSessionIds();
		List<ClaudeSessionSummaryModel> summaries;

		if (global)
		{
			if (string.IsNullOrEmpty(query))
				throw new TdcpInvalidParamsException(Constants.Import.QueryRequired);

			summaries = await FindAcrossProjectsAsync(query, cancellationToken).ConfigureAwait(false);
		}
		else
		{
			if (string.IsNullOrEmpty(directoryId))
				throw new TdcpInvalidParamsException(Constants.Import.SourceRequired);

			string path;
			lock (_settings.SyncRoot)
				path = (_registry.FindDirectoryByNodeId(directoryId) ?? throw new TdcpException(Constants.Import.DirectoryNotFound)).Path;

			summaries = [.. await Task.Run(() => _importer.DiscoverSessions(path), cancellationToken).ConfigureAwait(false)];
			if (!string.IsNullOrEmpty(query))
				summaries = [.. summaries.Where(s => Matches(s, query))];
		}

		var page = summaries
			.Skip(Math.Max(0, offset))
			.Take(Math.Clamp(limit, 1, Constants.Import.ListMaxLimit))
			.Select(s => ToCandidate(s, already))
			.ToList();
		return new ImportListResult(page, summaries.Count);
	}

	private async Task<List<ClaudeSessionSummaryModel>> FindAcrossProjectsAsync(string query, CancellationToken cancellationToken)
	{
		var hits = await _index.SearchAsync(query, null, null, Constants.Import.GlobalSearchMessages, 0, cancellationToken).ConfigureAwait(false);
		var ids  = hits.Select(h => h.ExternalId).Distinct(StringComparer.Ordinal).ToList(); // rank order, first (best) hit of a session wins
		if (ids.Count == 0)
			return [];

		var found = (await _index.GetSessionSummariesAsync(ids, cancellationToken).ConfigureAwait(false)).ToDictionary(s => s.SessionId, StringComparer.Ordinal);
		return [.. ids.Where(found.ContainsKey).Select(id => found[id])];
	}

	private static bool Matches(ClaudeSessionSummaryModel summary, string query) =>
		(summary.GeneratedTitle?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
		|| (summary.FirstUserPrompt?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

	private static ImportCandidateDto ToCandidate(ClaudeSessionSummaryModel summary, IReadOnlySet<string> already) => new(
		summary.SessionId,
		summary.GeneratedTitle,
		Cut(summary.FirstUserPrompt, Constants.Import.FirstPromptMaxChars),
		summary.LastUsed,
		summary.MessageCount,
		summary.MessageCount == 0,
		already.Contains(summary.SessionId),
		summary.ProjectSlug ?? SlugOf(summary.JsonlPath));

	private static string SlugOf(string? jsonlPath)
	{
		var directory = string.IsNullOrEmpty(jsonlPath) ? null : Path.GetDirectoryName(jsonlPath);
		return directory == null ? string.Empty : Path.GetFileName(directory);
	}

	private static string? Cut(string? text, int max) => text is { Length: > 0 } && text.Length > max ? text[..max] : text;

	// ── Titles ───────────────────────────────────────────────────────────────────────

	/// <inheritdoc />
	public ImportTitlesResult GetTitles(IReadOnlyList<string> externalIds)
	{
		if (externalIds.Count > Constants.Import.TitlesMaxIds)
			throw new TdcpInvalidParamsException(Constants.Import.TooManyIds);

		var ids = externalIds.Select(RequireExternalId).Distinct(StringComparer.Ordinal).ToList();
		var titles  = new List<ImportTitleDto>();
		var missing = new List<string>();
		foreach (var id in ids)
		{
			if (_importer.GetCachedTitle(id) is { } title)
				titles.Add(new ImportTitleDto(id, title));
			else
				missing.Add(id);
		}

		var started = missing.Where(id => _titleWork.TryAdd(id, 0)).ToList();
		if (started.Count > 0)
			_ = Task.Run(() => GenerateTitlesAsync(started));

		return new ImportTitlesResult(titles, missing.Count(_titleWork.ContainsKey));
	}

	/// <summary>Looks the sessions up in the index and asks it for their titles (a persisted title costs nothing; a new one is generated); every title lands in the import cache.</summary>
	private async Task GenerateTitlesAsync(IReadOnlyList<string> ids)
	{
		try
		{
			var summaries = (await _index.GetSessionSummariesAsync(ids).ConfigureAwait(false))
				.Where(s => s.FirstUserPrompt != null && s.GeneratedTitle == null)
				.ToList();
			if (summaries.Count > 0)
			{
				var all = await _index.EnsureTitlesAsync(summaries, OnBatch).ConfigureAwait(false);
				OnBatch(all);
			}
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "GenerateTitles: title generation failed");
		}
		finally
		{
			foreach (var id in ids)
				_titleWork.TryRemove(id, out _);
		}
	}

	private void OnBatch(IReadOnlyDictionary<string, string> titles)
	{
		foreach (var (id, title) in titles)
			_importer.CacheTitle(id, title);
	}

	private static string RequireExternalId(string id) =>
		ExternalIdPattern().IsMatch(id) ? id : throw new TdcpInvalidParamsException(Constants.Import.InvalidExternalId);

	// ── Run ──────────────────────────────────────────────────────────────────────────

	/// <inheritdoc />
	public async Task<ImportRunResult> RunAsync(string targetId, IReadOnlyList<ImportRequestDto> items, CancellationToken cancellationToken = default)
	{
		if (items.Count > Constants.Import.RunMaxItems)
			throw new TdcpInvalidParamsException(Constants.Import.TooManyItems);

		foreach (var item in items)
		{
			RequireExternalId(item.ExternalId);
			if (item.Name is { Length: > Constants.Import.NameMaxChars })
				throw new TdcpInvalidParamsException(Constants.Import.NameTooLong);
		}

		DirectoryNodeModel? directory;
		GroupNodeModel? group = null;
		lock (_settings.SyncRoot)
		{
			directory = _registry.FindDirectoryByNodeId(targetId);
			if (directory == null)
				group = _registry.FindGroupByNodeId(targetId);
		}

		if (directory == null && group == null)
			throw new TdcpException(Constants.Import.TargetNotFound);

		var indexed = (await _index.GetSessionSummariesAsync([.. items.Select(i => i.ExternalId).Distinct(StringComparer.Ordinal)], cancellationToken).ConfigureAwait(false))
			.ToDictionary(s => s.SessionId, StringComparer.Ordinal);

		return await Task.Run(() => Run(directory, group, items, indexed), cancellationToken).ConfigureAwait(false);
	}

	private ImportRunResult Run(DirectoryNodeModel? directory, GroupNodeModel? group, IReadOnlyList<ImportRequestDto> items, IReadOnlyDictionary<string, ClaudeSessionSummaryModel> indexed)
	{
		var imported = new List<ImportedSessionDto>();
		var failed   = new List<ImportFailureDto>();
		var seen     = new HashSet<string>(StringComparer.Ordinal);
		var already  = _registry.CollectAllClaudeSessionIds();

		foreach (var item in items)
		{
			if (!seen.Add(item.ExternalId))
				failed.Add(new ImportFailureDto(item.ExternalId, Constants.Import.DuplicateRequest));
			else if (already.Contains(item.ExternalId))
				failed.Add(new ImportFailureDto(item.ExternalId, Constants.Import.AlreadyImported));
			else if (FindSummary(item.ExternalId, indexed) is not { } summary)
				failed.Add(new ImportFailureDto(item.ExternalId, Constants.Import.SessionNotFound));
			else
				Record(ImportSession(directory, group, summary, item.Name), item.ExternalId, imported, failed);
		}

		return new ImportRunResult(imported, failed);
	}

	private static void Record(ImportOutcomeModel outcome, string externalId, List<ImportedSessionDto> imported, List<ImportFailureDto> failed)
	{
		if (outcome.Node != null)
			imported.Add(new ImportedSessionDto(outcome.Node.NodeId, externalId));
		else
			failed.Add(new ImportFailureDto(externalId, outcome.Failure == ImportFailureKind.Empty ? Constants.Import.SessionEmpty : Constants.Import.ImportFailed));
	}

	/// <summary>The summary the index holds when its file is still there, else one read from the file found under a Claude project folder.</summary>
	private ClaudeSessionSummaryModel? FindSummary(string externalId, IReadOnlyDictionary<string, ClaudeSessionSummaryModel> indexed)
	{
		if (indexed.TryGetValue(externalId, out var known) && !string.IsNullOrEmpty(known.JsonlPath) && File.Exists(known.JsonlPath))
		{
			known.GeneratedTitle ??= _importer.GetCachedTitle(externalId);
			return known;
		}

		var path = FindJsonlPath(externalId);
		if (path == null)
			return null;

		var summary = _importer.ExtractSummary(externalId, path);
		if (summary != null)
			summary.GeneratedTitle ??= _importer.GetCachedTitle(externalId);
		return summary;
	}

	private string? FindJsonlPath(string externalId)
	{
		var fileName = externalId + Constants.ClaudeSessions.SessionFileExtension;
		foreach (var projectsDirectory in _projectsDirsOverride ?? Constants.ClaudeSessions.GetAllProjectsDirs())
		{
			if (!Directory.Exists(projectsDirectory))
				continue;

			foreach (var projectDirectory in Directory.EnumerateDirectories(projectsDirectory))
			{
				var candidate = Path.Combine(projectDirectory, fileName);
				if (File.Exists(candidate))
					return candidate;
			}
		}

		return null;
	}
}
