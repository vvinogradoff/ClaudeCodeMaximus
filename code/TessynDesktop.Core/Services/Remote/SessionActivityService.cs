using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionActivityService" />
/// <remarks>
/// Prompt and response times come from the entries of the session file: the last
/// <see cref="Constants.Remote.ActivityTailBytes"/> bytes are parsed, and the whole file only when that tail holds no USER
/// entry and the file is larger. A response older than the tail's last prompt may therefore read as missing, which never
/// changes <see cref="SessionActivityModel.LastActivityUtc"/>. Created by Claude.
/// </remarks>
public sealed class SessionActivityService : ISessionActivityService
{
	private static readonly ILogger _log = Log.ForContext<SessionActivityService>();

	private readonly ISessionFileService _files;
	private readonly IDraftService _drafts;
	private readonly ConcurrentDictionary<string, CachedActivity> _cache = new(StringComparer.OrdinalIgnoreCase);

	public SessionActivityService(ISessionFileService files, IDraftService drafts)
	{
		_files  = files;
		_drafts = drafts;
	}

	/// <inheritdoc />
	public SessionActivityModel GetActivity(SessionNodeModel node)
	{
		var fileActivity = ReadFileActivity(node.FileName);
		var (hasDraft, draftTime) = ReadDraft(node.FileName);

		var lastActivity = Max(Max(fileActivity.LastPrompt, fileActivity.LastResponse), hasDraft ? draftTime : null);
		return new SessionActivityModel(lastActivity, fileActivity.LastPrompt, fileActivity.LastResponse, fileActivity.PromptProfileName, hasDraft);
	}

	private CachedActivity ReadFileActivity(string fileName)
	{
		try
		{
			var stamp = _files.GetFileStamp(fileName);
			if (stamp == null)
			{
				_cache.TryRemove(fileName, out _);
				return CachedActivity.None;
			}

			if (_cache.TryGetValue(fileName, out var cached) && cached.Stamp == stamp)
				return cached;

			var computed = Compute(fileName, stamp);
			_cache[fileName] = computed;
			return computed;
		}
		catch (Exception ex)
		{
			_log.Debug(ex, "SessionActivityService: could not read {FileName}", fileName);
			return CachedActivity.None;
		}
	}

	private CachedActivity Compute(string fileName, SessionFileStampModel stamp)
	{
		var entries = _files.ReadTailEntries(fileName, Constants.Remote.ActivityTailBytes);
		if (stamp.Length > Constants.Remote.ActivityTailBytes && !ContainsRole(entries, Constants.SessionFile.RoleUser))
			entries = _files.ReadEntries(fileName);

		SessionEntryModel? lastUser = null;
		DateTimeOffset? lastResponse = null;
		for (var i = entries.Count - 1; i >= 0 && (lastUser == null || lastResponse == null); i--)
		{
			if (lastUser == null && entries[i].Role == Constants.SessionFile.RoleUser)
				lastUser = entries[i];
			else if (lastResponse == null && entries[i].Role == Constants.SessionFile.RoleAssistant)
				lastResponse = entries[i].Timestamp;
		}

		return new CachedActivity(stamp, lastUser?.Timestamp, lastResponse, lastUser?.ProfileName);
	}

	private (bool HasDraft, DateTimeOffset? Time) ReadDraft(string fileName)
	{
		try
		{
			var draft = _drafts.LoadDraft(fileName);
			if (string.IsNullOrWhiteSpace(draft))
				return (false, null);

			return (true, _drafts.GetDraftTimestamp(fileName));
		}
		catch (Exception ex)
		{
			_log.Debug(ex, "SessionActivityService: could not read the draft of {FileName}", fileName);
			return (false, null);
		}
	}

	private static bool ContainsRole(IReadOnlyList<SessionEntryModel> entries, string role)
	{
		foreach (var entry in entries)
		{
			if (entry.Role == role)
				return true;
		}
		return false;
	}

	private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) =>
		a.HasValue && b.HasValue ? (a > b ? a : b) : a ?? b;

	private sealed record CachedActivity(SessionFileStampModel? Stamp, DateTimeOffset? LastPrompt, DateTimeOffset? LastResponse, string? PromptProfileName)
	{
		public static readonly CachedActivity None = new(null, null, null, null);
	}
}
