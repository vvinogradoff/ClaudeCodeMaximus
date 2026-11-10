using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Counts the displayable entries of a session's Claude JSONL: the baseline the JSONL write-back
/// compares against so it never re-appends entries a turn already wrote (ISSUE.009, ISSUE.014).
/// Does a full parse, so callers run it off the UI thread.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class JsonlEntryCounter
{
	private static readonly ILogger _log = Log.ForContext<JsonlEntryCounter>();

	private readonly IClaudeSessionImportService _importService;

	public JsonlEntryCounter(IClaudeSessionImportService importService)
	{
		_importService = importService;
	}

	/// <summary>
	/// Returns the number of displayable entries (not compaction separators, non-blank content) in the
	/// session's JSONL, or null when there is no session id, working directory or JSONL file, or the file
	/// cannot be read; the caller then falls back to its own entry count.
	/// </summary>
	public int? CountDisplayableEntries(string workingDirectory, string? sessionId, IReadOnlyList<string>? projectsDirs = null)
	{
		if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(workingDirectory))
			return null;

		try
		{
			var jsonlPath = Constants.ClaudeSessions.FindExistingJsonlPath(workingDirectory, sessionId, projectsDirs);
			if (jsonlPath == null || !File.Exists(jsonlPath))
				return null;

			var count = 0;
			foreach (var entry in _importService.ParseJsonlSession(jsonlPath))
			{
				if (entry.Role != Constants.SessionFile.RoleCompaction && !string.IsNullOrWhiteSpace(entry.Content))
					count++;
			}

			_log.Debug("JsonlEntryCounter: {Count} displayable entries in JSONL for session {SessionId}", count, sessionId);
			return count;
		}
		catch (Exception ex)
		{
			_log.Debug("JsonlEntryCounter: error reading JSONL: {Error}", ex.Message);
			return null;
		}
	}
}
