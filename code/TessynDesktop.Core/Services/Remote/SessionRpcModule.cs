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
		IClaudeStreamingSessionManager streaming)
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
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.Add(Constants.Remote.Methods.SessionGet, async (p, _) => await GetAsync(p), remoteCallable: true);
		registry.AddSync(Constants.Remote.Methods.SessionEntries, Entries, remoteCallable: true);
		registry.AddSync(Constants.Remote.Methods.SessionEntry, Entry, remoteCallable: true);
		registry.Add(Constants.Remote.Methods.SessionSend, async (p, _) => await SendAsync(p), remoteCallable: true);
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
		var entries  = _entries.GetAll(node.FileName);
		var lastUsed = LastUsedSettingsResolver.Resolve(entries, _models.GetCachedModels(), profiles, await _labels.GetDefaultLabelAsync());

		return new SessionDetailDto(
			node.NodeId,
			name,
			directoryId,
			groupId,
			new SessionLocationDto(location.directoryLabel, location.treePath),
			supervisorId,
			_stateBuilder.Build(node),
			_tags.GetTags(node.NodeId, name).Select(tag => new SessionTagDto(tag)).ToList(),
			new AttentionSummaryDto(_attention.GetSummary(node.NodeId)),
			lastUsed,
			isStreamingMode,
			entries.Count,
			epoch);
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
		var beforeIndex = TdcpParams.OptionalInt(@params, "beforeIndex");
		var afterIndex  = TdcpParams.OptionalInt(@params, "afterIndex");
		var limit       = TdcpParams.OptionalInt(@params, "limit") ?? Constants.Remote.Limits.EntriesDefaultLimit;

		if (beforeIndex.HasValue && afterIndex.HasValue)
			throw new TdcpInvalidParamsException("'beforeIndex' and 'afterIndex' cannot be combined");

		if (limit < 1)
			throw new TdcpInvalidParamsException("'limit' must be at least 1");

		limit = System.Math.Min(limit, Constants.Remote.Limits.EntriesMaxLimit);
		return _entries.GetPage(node.NodeId, node.FileName, beforeIndex, afterIndex, limit);
	}

	private object? Entry(JsonNode? @params)
	{
		var node  = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));
		var index = TdcpParams.OptionalInt(@params, "index") ?? throw new TdcpInvalidParamsException("'index' is required");
		var epoch = TdcpParams.OptionalInt(@params, "epoch") ?? throw new TdcpInvalidParamsException("'epoch' is required");

		return _entries.GetEntry(node.FileName, index, epoch);
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

		var cancelled = _turns.CancelTurn(node.NodeId);
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
