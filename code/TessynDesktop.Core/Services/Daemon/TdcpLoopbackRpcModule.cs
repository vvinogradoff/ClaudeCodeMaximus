using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// The original TDCP methods (tree, turn, schedule and content access). They hand out raw domain models and local
/// paths, so none of them is remote-callable: they exist for the token-authenticated loopback endpoint only.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TdcpLoopbackRpcModule : IRemoteRpcModule
{
	private readonly IDaemonClient _client;

	public TdcpLoopbackRpcModule(IDaemonClient client)
	{
		_client = client;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync("tree.list",           _ => TreeList(),                  remoteCallable: false);
		registry.AddSync("tree.getSelected",    _ => TreeGetSelected(),           remoteCallable: false);
		registry.AddSync("tree.setSelected",    TreeSetSelected,                  remoteCallable: false);
		registry.AddSync("tree.findByNodeId",   TreeFindByNodeId,                 remoteCallable: false);
		registry.AddSync("tree.findByToken",    TreeFindByToken,                  remoteCallable: false);
		registry.AddSync("tree.location",       TreeLocation,                     remoteCallable: false);
		registry.AddSync("turn.isRunning",      TurnIsRunning,                    remoteCallable: false);
		registry.AddSync("turn.setRunning",     TurnSetRunning,                   remoteCallable: false);
		registry.Add    ("turn.run",            (p, _) => TurnRunAsync(p),        remoteCallable: false);
		registry.AddSync("turn.cancel",         TurnCancel,                       remoteCallable: false);
		registry.AddSync("turn.resolveProfile", TurnResolveProfile,               remoteCallable: false);
		registry.AddSync("schedule.list",       ScheduleList,                     remoteCallable: false);
		registry.AddSync("schedule.add",        ScheduleAdd,                      remoteCallable: false);
		registry.AddSync("schedule.remove",     ScheduleRemove,                   remoteCallable: false);
		registry.AddSync("content.read",        ContentRead,                      remoteCallable: false);
		registry.AddSync("content.exists",      ContentExists,                    remoteCallable: false);
		registry.AddSync("content.path",        ContentPath,                      remoteCallable: false);
	}

	// ── Tree methods ──────────────────────────────────────────────────────────────

	private object? TreeList() => _client.Directories;

	private object? TreeGetSelected() => new SelectedNodeIdResult(_client.SelectedNodeId);

	private object? TreeSetSelected(JsonNode? @params)
	{
		_client.SelectedNodeId = TdcpParams.OptionalString(@params, "nodeId");
		return new TdcpEmptyResult();
	}

	private object? TreeFindByNodeId(JsonNode? @params) =>
		_client.FindSessionByNodeId(TdcpParams.RequireString(@params, "nodeId"));

	private object? TreeFindByToken(JsonNode? @params) =>
		_client.FindSessionByAgentToken(TdcpParams.RequireString(@params, "token"));

	private object? TreeLocation(JsonNode? @params)
	{
		var node = RequireSession(@params);
		var (label, path) = _client.GetSessionLocation(node);
		return new LocationResult(label, path);
	}

	// ── Running state methods ─────────────────────────────────────────────────────

	private object? TurnIsRunning(JsonNode? @params) =>
		new IsRunningResult(_client.IsSessionRunning(TdcpParams.RequireString(@params, "nodeId")));

	private object? TurnSetRunning(JsonNode? @params)
	{
		var nodeId  = TdcpParams.RequireString(@params, "nodeId");
		var running = TdcpParams.RequireBool(@params, "running");
		_client.SetSessionRunning(nodeId, running);
		return new TdcpEmptyResult();
	}

	// ── Turn methods ──────────────────────────────────────────────────────────────

	private async Task<object?> TurnRunAsync(JsonNode? @params)
	{
		var prompt    = TdcpParams.RequireString(@params, "prompt");
		var sourceStr = TdcpParams.OptionalString(@params, "source") ?? "user";
		if (!Enum.TryParse<TurnSource>(sourceStr, ignoreCase: true, out var source))
			source = TurnSource.User;

		var node = RequireSession(@params);

		return await _client.RunTurnAsync(node, prompt, source);
	}

	private object? TurnCancel(JsonNode? @params) =>
		new CancelledResult(_client.CancelTurn(TdcpParams.RequireString(@params, "nodeId")));

	private object? TurnResolveProfile(JsonNode? @params) =>
		new ProfileNameResult(_client.ResolveProfileName(RequireSession(@params)));

	// ── Schedule methods ──────────────────────────────────────────────────────────

	private object? ScheduleList(JsonNode? @params) =>
		_client.GetSchedules(TdcpParams.OptionalString(@params, "targetNodeId"));

	private object? ScheduleAdd(JsonNode? @params)
	{
		var json     = @params?.ToJsonString() ?? throw new TdcpInvalidParamsException("'params' is required for schedule.add");
		var schedule = JsonSerializer.Deserialize<ScheduleModel>(json, TdcpJsonOptions.Default)
		               ?? throw new TdcpInvalidParamsException("Could not parse ScheduleModel from params");
		_client.AddSchedule(schedule);
		return new TdcpEmptyResult();
	}

	private object? ScheduleRemove(JsonNode? @params) =>
		new RemovedResult(_client.RemoveSchedule(TdcpParams.RequireString(@params, "scheduleId")));

	// ── Content methods ───────────────────────────────────────────────────────────

	private object? ContentRead(JsonNode? @params) =>
		(IReadOnlyList<SessionEntryModel>)_client.ReadEntries(TdcpParams.RequireString(@params, "fileName"));

	private object? ContentExists(JsonNode? @params) =>
		new ExistsResult(_client.SessionFileExists(TdcpParams.RequireString(@params, "fileName")));

	private object? ContentPath(JsonNode? @params) =>
		new FilePathResult(_client.GetSessionFilePath(TdcpParams.RequireString(@params, "fileName")));

	private SessionNodeModel RequireSession(JsonNode? @params)
	{
		var nodeId = TdcpParams.RequireString(@params, "nodeId");
		return _client.FindSessionByNodeId(nodeId) ?? throw new TdcpException($"Session not found: {nodeId}");
	}

	// ── Private result records ────────────────────────────────────────────────────
	// These are private nested types only used within this module.

	private sealed record SelectedNodeIdResult(string? NodeId);
	private sealed record LocationResult(string DirectoryLabel, string TreePath);
	private sealed record IsRunningResult(bool Running);
	private sealed record CancelledResult(bool Cancelled);
	private sealed record ProfileNameResult(string? ProfileName);
	private sealed record RemovedResult(bool Removed);
	private sealed record ExistsResult(bool Exists);
	private sealed record FilePathResult(string Path);
}
