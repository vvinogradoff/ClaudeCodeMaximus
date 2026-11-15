using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Remote methods of one session: details, paged transcript, sending and stopping turns, and the favourite and
/// streaming settings (contract sections 3.9 to 3.13, 3.15 and 3.16).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionRpcModule : IRemoteRpcModule
{
	private readonly ISessionRegistry _registry;
	private readonly IAppSettingsService _appSettings;
	private readonly ISessionStateBuilder _stateBuilder;
	private readonly ISessionTagService _tags;
	private readonly IAttentionService _attention;
	private readonly ISessionEntryReader _entries;
	private readonly ISessionEpochTracker _epochs;
	private readonly IClaudeModelService _models;
	private readonly IProfileLabelService _labels;
	private readonly ISessionSendService _send;
	private readonly ITurnOptionsResolver _options;
	private readonly ISessionTurnService _turns;
	private readonly IClaudeStreamingSessionManager _streaming;
	private readonly ITurnEngine? _engine;

	public SessionRpcModule(
		ISessionRegistry registry,
		IAppSettingsService appSettings,
		ISessionStateBuilder stateBuilder,
		ISessionTagService tags,
		IAttentionService attention,
		ISessionEntryReader entries,
		ISessionEpochTracker epochs,
		IClaudeModelService models,
		IProfileLabelService labels,
		ISessionSendService send,
		ITurnOptionsResolver options,
		ISessionTurnService turns,
		IClaudeStreamingSessionManager streaming,
		ITurnEngine? engine = null)
	{
		_registry     = registry;
		_appSettings  = appSettings;
		_stateBuilder = stateBuilder;
		_tags         = tags;
		_attention    = attention;
		_entries      = entries;
		_epochs       = epochs;
		_models       = models;
		_labels       = labels;
		_send         = send;
		_options      = options;
		_turns        = turns;
		_streaming    = streaming;
		_engine       = engine;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.Add(Constants.Remote.Methods.SessionGet, async (p, _) => await GetAsync(p), remoteCallable: true);
		registry.AddSync(Constants.Remote.Methods.SessionEntries, Entries, remoteCallable: true);
		registry.AddSync(Constants.Remote.Methods.SessionEntry, Entry, remoteCallable: true);
		registry.Add(Constants.Remote.Methods.SessionSend, async (p, _) => await SendAsync(p), remoteCallable: true);
		registry.Add(RemoteProtocol.LoopbackMethods.SessionHeaders, async (p, _) => await HeadersAsync(p), remoteCallable: false);
		registry.AddSync(Constants.Remote.Methods.SessionStop, Stop, remoteCallable: true);
		registry.AddSync(Constants.Remote.Methods.SessionSetFavorite, SetFavorite, remoteCallable: true);
		registry.AddSync(Constants.Remote.Methods.SessionSetStreaming, SetStreaming, remoteCallable: true);
	}

	// ── session.get ──────────────────────────────────────────────────────────

	private async Task<object?> GetAsync(JsonNode? @params)
	{
		var node = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));

		string name;
		string directoryId;
		string? groupId;
		bool isStreamingMode;
		string? supervisorId;
		ClaudeProfileModel[] profiles;
		lock (_appSettings.SyncRoot)
		{
			name            = node.Name;
			isStreamingMode = node.IsStreamingMode;
			(directoryId, groupId) = FindContainer(node);
			supervisorId    = node.SupervisorNodeId != null && _registry.FindModelByNodeId(node.SupervisorNodeId) != null
				? node.SupervisorNodeId
				: null;
			profiles        = [.. _appSettings.Settings.Profiles];
		}

		var location = _registry.GetSessionLocation(node);
		var epoch    = _epochs.GetEpoch(node.FileName);
		var content  = ReadContentHeader(node.FileName, profiles, _models.GetCachedModels(), await _labels.GetDefaultLabelAsync());

		return new SessionDetailDto(
			node.NodeId,
			name,
			directoryId,
			groupId,
			new SessionLocationDto(location.directoryLabel, location.treePath),
			supervisorId,
			_stateBuilder.Build(node),
			_tags.GetTags(node.NodeId, name).Select(tag => tag.ToDto()).ToList(),
			_attention.GetSummary(node.NodeId).ToDto(),
			content.LastUsed,
			isStreamingMode,
			content.EntryCount,
			epoch,
			content.FileId,
			content.Seq);
	}

	/// <summary>What <c>session.get</c> and <c>session.headers</c> need from the transcript.</summary>
	private sealed record ContentSummary(
		string? FileId,
		int EntryCount,
		long? Seq,
		DateTimeOffset? FirstTimestamp,
		DateTimeOffset? LastTimestamp,
		LastUsedSettingsDto LastUsed,
		long ContentVersion,
		long FileLength);

	/// <summary>
	/// P1.G: the summary comes from the content store's index (O(1), the last-used metadata by an in-memory walk and at most two
	/// header reads). Without a store (stub-based tests) it falls back to a full parse and reports no file id.
	/// </summary>
	private ContentSummary ReadContentHeader(string fileName, ClaudeProfileModel[] profiles, IReadOnlyList<ClaudeModelInfo> models, string label)
	{
		var header = _entries.GetHeader(fileName);
		SessionContentLastUsedModel? used = null;
		try
		{
			used = header == null ? null : _entries.GetLastUsed(fileName);
		}
		catch (TdcpException)
		{
			// Rapid rewrites: fall back to the full parse below instead of failing the call.
		}

		if (header == null || used == null)
		{
			var entries = _entries.GetAll(fileName);
			return new ContentSummary(
				null, entries.Count, null,
				entries.Count > 0 ? entries[0].Timestamp : null,
				entries.Count > 0 ? entries[^1].Timestamp : null,
				LastUsedSettingsResolver.Resolve(entries, models, profiles, label), 0, 0);
		}

		return new ContentSummary(
			header.Exists ? EntryIds.FormatFileId(header.FileId) : null,
			header.EntryCount,
			header.NextOrdinal,
			header.FirstTimestamp,
			header.LastTimestamp,
			LastUsedSettingsResolver.Resolve(used.ProfileName, used.ModelId, used.Effort, models, profiles, label),
			header.ContentVersion,
			header.FileLength);
	}

	// ── session.headers (loopback only) ──────────────────────────────────────

	private async Task<object?> HeadersAsync(JsonNode? @params)
	{
		if (TdcpParams.Find(@params, "nodeIds") is not JsonArray array)
			throw new TdcpInvalidParamsException("'nodeIds' must be an array of strings");

		var ids  = new List<string>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in array)
		{
			if (item is not JsonValue value || !value.TryGetValue<string>(out var id) || id.Length == 0)
				throw new TdcpInvalidParamsException("'nodeIds' must be an array of strings");

			if (seen.Add(id))
				ids.Add(id);
		}

		if (ids.Count > Constants.Remote.Limits.HeadersMaxNodeIds)
			throw new TdcpInvalidParamsException($"'nodeIds' holds at most {Constants.Remote.Limits.HeadersMaxNodeIds} ids");

		ClaudeProfileModel[] profiles;
		lock (_appSettings.SyncRoot)
			profiles = [.. _appSettings.Settings.Profiles];

		var models  = _models.GetCachedModels();
		var label   = await _labels.GetDefaultLabelAsync();
		var headers = new List<SessionHeaderDto>(ids.Count);
		List<string>? missing = null;
		foreach (var id in ids)
		{
			var node = _registry.FindModelByNodeId(id);
			if (node == null)
			{
				(missing ??= []).Add(id);
				continue;
			}

			var content = ReadContentHeader(node.FileName, profiles, models, label);
			headers.Add(new SessionHeaderDto(
				id,
				content.FileId,
				_epochs.GetEpoch(node.FileName),
				content.EntryCount,
				content.Seq ?? content.EntryCount,
				content.FirstTimestamp,
				content.LastTimestamp,
				content.LastUsed,
				content.ContentVersion,
				content.FileLength));
		}

		return new SessionHeadersResult(headers, missing);
	}

	/// <summary>The ids of the directory holding <paramref name="node"/> and of its immediate group. The caller holds the settings lock.</summary>
	private (string DirectoryId, string? GroupId) FindContainer(SessionNodeModel node)
	{
		foreach (var directory in _registry.Directories)
		{
			if (directory.Sessions.Contains(node))
				return (directory.NodeId, null);

			var group = FindGroup(directory.Groups, node);
			if (group != null)
				return (directory.NodeId, group.NodeId);
		}

		throw new TdcpException("Session not found");
	}

	private static GroupNodeModel? FindGroup(IEnumerable<GroupNodeModel> groups, SessionNodeModel node)
	{
		foreach (var group in groups)
		{
			if (group.Sessions.Contains(node))
				return group;

			var nested = FindGroup(group.Groups, node);
			if (nested != null)
				return nested;
		}

		return null;
	}

	// ── session.entries / session.entry ──────────────────────────────────────

	private object? Entries(JsonNode? @params)
	{
		var node        = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));
		var beforeIndex   = TdcpParams.OptionalInt(@params, "beforeIndex");
		var afterIndex    = TdcpParams.OptionalInt(@params, "afterIndex");
		var afterSeq      = TdcpParams.OptionalLong(@params, "afterSeq");
		var beforeEntryId = TdcpParams.OptionalString(@params, "beforeEntryId");
		var aroundEntryId = TdcpParams.OptionalString(@params, "aroundEntryId");
		var fileIdText    = TdcpParams.OptionalString(@params, "fileId");
		var limit         = TdcpParams.OptionalInt(@params, "limit") ?? Constants.Remote.Limits.EntriesDefaultLimit;

		if (beforeIndex.HasValue && afterIndex.HasValue)
			throw new TdcpInvalidParamsException("'beforeIndex' and 'afterIndex' cannot be combined");

		// P1.F: afterSeq and beforeEntryId are alternatives to afterIndex and beforeIndex, not additions.
		var bounds = (beforeIndex.HasValue ? 1 : 0) + (afterIndex.HasValue ? 1 : 0) + (afterSeq.HasValue ? 1 : 0) + (beforeEntryId != null ? 1 : 0)
			+ (aroundEntryId != null ? 1 : 0);
		if (bounds > 1)
			throw new TdcpInvalidParamsException("'beforeIndex', 'afterIndex', 'afterSeq', 'beforeEntryId' and 'aroundEntryId' cannot be combined");

		if (fileIdText != null && !afterSeq.HasValue)
			throw new TdcpInvalidParamsException("'fileId' is only valid together with 'afterSeq'");

		if (limit < 1)
			throw new TdcpInvalidParamsException("'limit' must be at least 1");

		limit = System.Math.Min(limit, Constants.Remote.Limits.EntriesMaxLimit);

		if (afterSeq.HasValue)
		{
			if (afterSeq.Value < 0)
				throw new TdcpInvalidParamsException("'afterSeq' must not be negative");

			// seq counts the entries a client holds, so the entries after it start at ordinal seq: afterIndex = seq - 1.
			var after = (int)System.Math.Min(afterSeq.Value - 1, int.MaxValue);
			if (fileIdText == null)
				return _entries.GetPage(node.NodeId, node.FileName, null, after, limit);

			if (!EntryIds.TryParseFileId(fileIdText, out var seqFileId))
				throw new TdcpInvalidParamsException("'fileId' is not a file id");

			return _entries.GetPage(node.NodeId, node.FileName, null, after, limit, seqFileId);
		}

		if (aroundEntryId != null)
		{
			if (!EntryIds.TryParse(aroundEntryId, out var aroundFileId, out var aroundOrdinal))
				throw new TdcpInvalidParamsException("'aroundEntryId' is not an entry id");

			// The page is centred on the entry (clamped to the file, so it stays full near either end); the entry's id carries the
			// content generation, so a rewritten file fails with "Session history changed" instead of centring on another entry.
			var header = _entries.GetHeader(node.FileName)
				?? throw new TdcpInvalidParamsException("Entry ids are not available");
			if (header.Exists && header.FileId != aroundFileId)
				throw new TdcpException("Session history changed");
			if (!header.Exists || aroundOrdinal >= header.EntryCount)
				throw new TdcpException("Entry not found");

			var start = System.Math.Clamp(aroundOrdinal - limit / 2, 0, System.Math.Max(0, header.EntryCount - limit));
			return _entries.GetPage(node.NodeId, node.FileName, null, start - 1, limit, aroundFileId);
		}

		if (beforeEntryId != null)
		{
			if (!EntryIds.TryParse(beforeEntryId, out var fileId, out var ordinal))
				throw new TdcpInvalidParamsException("'beforeEntryId' is not an entry id");

			return _entries.GetPage(node.NodeId, node.FileName, ordinal, null, limit, fileId);
		}

		return _entries.GetPage(node.NodeId, node.FileName, beforeIndex, afterIndex, limit);
	}

	private object? Entry(JsonNode? @params)
	{
		var node    = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));
		var entryId = TdcpParams.OptionalString(@params, "entryId");
		var range   = ParseRange(TdcpParams.Find(@params, "range"));

		SessionEntryDto entry;
		if (entryId != null)
		{
			// P1.F: by stable id; the id carries the content generation, so no epoch is needed.
			if (TdcpParams.Find(@params, "index") != null || TdcpParams.Find(@params, "epoch") != null)
				throw new TdcpInvalidParamsException("'entryId' cannot be combined with 'index' or 'epoch'");

			if (!EntryIds.TryParse(entryId, out var fileId, out var ordinal))
				throw new TdcpInvalidParamsException("'entryId' is not an entry id");

			entry = _entries.GetEntryById(node.FileName, fileId, ordinal);
		}
		else
		{
			var index = TdcpParams.OptionalInt(@params, "index") ?? throw new TdcpInvalidParamsException("'index' is required");
			var epoch = TdcpParams.OptionalInt(@params, "epoch") ?? throw new TdcpInvalidParamsException("'epoch' is required");
			entry = _entries.GetEntry(node.FileName, index, epoch);
		}

		return range == null ? entry : SliceContent(entry, range);
	}

	private static EntryRangeParams? ParseRange(JsonNode? node)
	{
		if (node == null)
			return null;

		if (node is not JsonObject)
			throw new TdcpInvalidParamsException("'range' must be an object { offset, length }");

		var offset = TdcpParams.OptionalInt(node, "offset") ?? 0;
		var length = TdcpParams.OptionalInt(node, "length") ?? Constants.Remote.Limits.EntryContentMaxChars;
		if (offset < 0)
			throw new TdcpInvalidParamsException("'range.offset' must not be negative");

		if (length < 1)
			throw new TdcpInvalidParamsException("'range.length' must be at least 1");

		return new EntryRangeParams(offset, System.Math.Min(length, Constants.Remote.Limits.EntryContentMaxChars));
	}

	/// <summary>
	/// A slice of the content in characters. Both ends are moved off a surrogate pair so the slice is valid text; a client that
	/// continues at <c>contentOffset + content.length</c> loses and repeats nothing.
	/// </summary>
	private static SessionEntryDto SliceContent(SessionEntryDto entry, EntryRangeParams range)
	{
		var content = entry.Content;
		var full    = content.Length;
		var start   = System.Math.Min(range.Offset, full);
		if (start > 0 && start < full && char.IsLowSurrogate(content[start]) && char.IsHighSurrogate(content[start - 1]))
			start--;

		var end = (int)System.Math.Min((long)full, (long)start + (range.Length ?? Constants.Remote.Limits.EntryContentMaxChars));
		if (end < full && char.IsHighSurrogate(content[end - 1]) && char.IsLowSurrogate(content[end]))
			end = end > start + 1 ? end - 1 : end + 1; // one character of the pair must not be cut off; a 1-char slice takes both halves

		return entry with
		{
			Content       = content[start..end],
			ContentOffset = start,
			Truncated     = start > 0 || end < full ? true : null,
			FullLength    = full,
		};
	}

	// ── session.send / session.stop ──────────────────────────────────────────

	private async Task<object?> SendAsync(JsonNode? @params)
	{
		var node = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));
		var text = TdcpParams.RequireString(@params, "text").Trim();
		if (text.Length == 0)
			throw new TdcpInvalidParamsException("'text' must not be blank");

		var options = await _options.ResolveAsync(@params);
		var queued  = _send.Send(node, text, options);

		return new SessionSendResult(Accepted: true, Queued: queued);
	}

	private object? Stop(JsonNode? @params)
	{
		var node = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));

		var processAlive = _streaming.IsAlive(node.NodeId);
		if (processAlive)
			_streaming.Stop(node.NodeId);

		// A turn the engine runs (started with turn.send) is stopped here too.
		var cancelled = _turns.CancelTurn(node.NodeId) | (_engine?.CancelRuns(node.NodeId) ?? false);
		return new SessionStopResult(processAlive || cancelled);
	}

	// ── session.setFavorite / session.setStreaming ───────────────────────────

	private object? SetFavorite(JsonNode? @params)
	{
		var node  = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));
		var value = TdcpParams.RequireBool(@params, "value");

		bool changed;
		lock (_appSettings.SyncRoot)
		{
			changed = node.IsFavorite != value;
			if (changed)
			{
				node.IsFavorite = value;
				_appSettings.Save();
			}
		}

		if (changed)
			_registry.RaiseSessionSettingsChanged(node.NodeId);

		return new SetFavoriteResult(value);
	}

	private object? SetStreaming(JsonNode? @params)
	{
		var node    = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));
		var enabled = TdcpParams.RequireBool(@params, "enabled");
		var force   = TdcpParams.OptionalBool(@params, "force") ?? false;

		var processStopped = false;
		if (!enabled && _streaming.IsAlive(node.NodeId))
		{
			if (!force)
				return new SetStreamingResult(Enabled: node.IsStreamingMode, NeedsConfirm: true);

			_streaming.Stop(node.NodeId);
			processStopped = true;
		}

		bool changed;
		lock (_appSettings.SyncRoot)
		{
			changed = node.IsStreamingMode != enabled;
			if (changed)
			{
				node.IsStreamingMode = enabled;
				_appSettings.Save();
			}
		}

		if (changed || processStopped)
			_registry.RaiseSessionSettingsChanged(node.NodeId);

		return new SetStreamingResult(enabled, NeedsConfirm: false);
	}
}
