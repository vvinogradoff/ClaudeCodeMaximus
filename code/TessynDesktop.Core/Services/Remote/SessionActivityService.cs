using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionActivityService" />
/// <remarks>
/// Prompt and response times come from the entries of the session file. A cold entry parses the last
/// <see cref="Constants.Remote.ActivityTailBytes"/> bytes, widened once to <see cref="Constants.Remote.ActivityColdParseMaxBytes"/>
/// when that tail holds no USER entry (beyond it the last prompt reads as unknown). A file that only grew since the cached read
/// parses just the appended bytes and merges them into the cached values; a shorter or same-length rewrite resets the entry.
/// Draft presence is cached by the draft file's stamp, so a draft is read only when it changed. The Focus reply drafts (FR.9.20) add to the
/// last-activity time only, cached per item by stamp the same way; they never set <c>HasDraft</c>. Created by Claude.
/// </remarks>
public sealed class SessionActivityService : ISessionActivityService
{
	private static readonly ILogger _log = Log.ForContext<SessionActivityService>();

	private readonly ISessionFileService _files;
	private readonly IDraftService _drafts;
	private readonly ConcurrentDictionary<string, CachedActivity> _cache = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, CachedDraft> _draftCache = new(StringComparer.OrdinalIgnoreCase);

	// Per session, the reply drafts seen at the last call (item id -> cached read). The dictionary is replaced as a whole, never mutated after publishing.
	private readonly ConcurrentDictionary<string, Dictionary<string, CachedDraft>> _replyCache = new(StringComparer.OrdinalIgnoreCase);

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

		// FR.9.20: a non-blank Focus reply draft counts for the time like the composer draft, but never for HasDraft (the pencil, "drafts first").
		var replyTime = ReadReplyDraftTime(node.FileName);

		var lastActivity = Max(Max(Max(fileActivity.LastPrompt, fileActivity.LastResponse), hasDraft ? draftTime : null), replyTime);
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

			_cache.TryGetValue(fileName, out var cached);
			if (cached != null && cached.Stamp == stamp)
				return cached;

			var computed = cached?.Stamp != null && stamp.Length > cached.Stamp.Length
				? Append(fileName, stamp, cached)
				: Compute(fileName, stamp);
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
			entries = _files.ReadTailEntries(fileName, Constants.Remote.ActivityColdParseMaxBytes);

		return Scan(entries, stamp, previous: null);
	}

	/// <summary>The file only grew: parse the bytes after the cached length and let the newer values win.</summary>
	private CachedActivity Append(string fileName, SessionFileStampModel stamp, CachedActivity previous) =>
		Scan(_files.ReadEntriesFrom(fileName, previous.Stamp!.Length), stamp, previous);

	private static CachedActivity Scan(IReadOnlyList<SessionEntryModel> entries, SessionFileStampModel stamp, CachedActivity? previous)
	{
		SessionEntryModel? lastUser = null;
		DateTimeOffset? lastResponse = null;
		for (var i = entries.Count - 1; i >= 0 && (lastUser == null || lastResponse == null); i--)
		{
			if (lastUser == null && entries[i].Role == Constants.SessionFile.RoleUser)
				lastUser = entries[i];
			else if (lastResponse == null && entries[i].Role == Constants.SessionFile.RoleAssistant)
				lastResponse = entries[i].Timestamp;
		}

		if (lastUser != null)
			return new CachedActivity(stamp, lastUser.Timestamp, lastResponse ?? previous?.LastResponse, lastUser.ProfileName);

		return new CachedActivity(stamp, previous?.LastPrompt, lastResponse ?? previous?.LastResponse, previous?.PromptProfileName);
	}

	private (bool HasDraft, DateTimeOffset? Time) ReadDraft(string fileName)
	{
		try
		{
			var stamp = _drafts.GetDraftStamp(fileName);
			if (stamp == null)
			{
				_draftCache.TryRemove(fileName, out _);
				return (false, null);
			}

			if (_draftCache.TryGetValue(fileName, out var cached) && cached.Stamp == stamp)
				return (cached.HasDraft, cached.Time);

			var draft = _drafts.LoadDraft(fileName);
			var hasDraft = !string.IsNullOrWhiteSpace(draft);
			var time = hasDraft ? _drafts.GetDraftTimestamp(fileName) : null;
			_draftCache[fileName] = new CachedDraft(stamp, hasDraft, time);
			return (hasDraft, time);
		}
		catch (Exception ex)
		{
			_log.Debug(ex, "SessionActivityService: could not read the draft of {FileName}", fileName);
			return (false, null);
		}
	}

	/// <summary>The latest write time over the session's non-blank Focus reply drafts, or null. Lists the files (metadata) and reads a draft only when its stamp changed.</summary>
	private DateTimeOffset? ReadReplyDraftTime(string fileName)
	{
		try
		{
			var stamps = _drafts.ListItemDraftStamps(fileName);
			if (stamps.Count == 0)
			{
				_replyCache.TryRemove(fileName, out _);
				return null;
			}

			_replyCache.TryGetValue(fileName, out var previous);
			var current = new Dictionary<string, CachedDraft>(stamps.Count, StringComparer.Ordinal);
			DateTimeOffset? latest = null;
			foreach (var item in stamps)
			{
				if (previous == null || !previous.TryGetValue(item.ItemId, out var entry) || entry.Stamp != item.Stamp)
				{
					var hasText = !string.IsNullOrWhiteSpace(_drafts.LoadItemDraft(fileName, item.ItemId));
					entry = new CachedDraft(item.Stamp, hasText, hasText ? item.Stamp.LastWriteUtc : null);
				}

				current[item.ItemId] = entry;
				latest = Max(latest, entry.Time);
			}

			_replyCache[fileName] = current; // items no longer listed drop out of the cache here
			return latest;
		}
		catch (Exception ex)
		{
			_log.Debug(ex, "SessionActivityService: could not read the reply drafts of {FileName}", fileName);
			return null;
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

	private sealed record CachedDraft(SessionFileStampModel Stamp, bool HasDraft, DateTimeOffset? Time);

	private sealed record CachedActivity(SessionFileStampModel? Stamp, DateTimeOffset? LastPrompt, DateTimeOffset? LastResponse, string? PromptProfileName)
	{
		public static readonly CachedActivity None = new(null, null, null, null);
	}
}
