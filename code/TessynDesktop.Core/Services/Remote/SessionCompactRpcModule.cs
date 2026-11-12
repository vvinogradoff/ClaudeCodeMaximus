using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>Remote-callable <c>session.compact</c> (contract section 3.14): starts <see cref="ISessionCompactionService"/> in the background.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionCompactRpcModule : IRemoteRpcModule
{
	private static readonly ILogger _log = Log.ForContext<SessionCompactRpcModule>();

	private readonly ISessionRegistry          _registry;
	private readonly ISessionCompactionService _compaction;
	private readonly ISessionTurnService       _turnService;
	private readonly ISessionFileService       _fileService;
	private readonly IAppSettingsService       _appSettings;
	private readonly IClaudeModelService       _models;
	private readonly IClaudeProfileService     _profiles;
	private readonly ITurnOptionsResolver      _optionsResolver;

	public SessionCompactRpcModule(
		ISessionRegistry registry,
		ISessionCompactionService compaction,
		ISessionTurnService turnService,
		ISessionFileService fileService,
		IAppSettingsService appSettings,
		IClaudeModelService models,
		IClaudeProfileService profiles,
		ITurnOptionsResolver optionsResolver)
	{
		_registry        = registry;
		_compaction      = compaction;
		_turnService     = turnService;
		_fileService     = fileService;
		_appSettings     = appSettings;
		_models          = models;
		_profiles        = profiles;
		_optionsResolver = optionsResolver;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry) =>
		registry.Add(Constants.Remote.Methods.SessionCompact, (p, _) => CompactAsync(p), remoteCallable: true);

	private async Task<object?> CompactAsync(JsonNode? @params)
	{
		var nodeId = TdcpParams.RequireString(@params, "nodeId");

		// The same validation as session.send: unknown model, effort or profile is -32602.
		var requested = await _optionsResolver.ResolveAsync(@params);

		var node = _registry.FindModelByNodeId(nodeId) ?? throw new TdcpException("Session not found");
		var options = ResolveOptions(node, requested);

		var queued = _registry.IsSessionRunning(nodeId);
		_ = Task.Run(() => RunAsync(node, options));
		return new SessionCompactResult(true, queued);
	}

	/// <summary>Turns the validated per-turn options into run options like a headless turn: absent model = node, directory, app setting; "default" = no flag; absent profile = the session's own.</summary>
	private SessionCompactionOptionsModel ResolveOptions(SessionNodeModel node, TurnOptionsModel? requested)
	{
		// May read the session file, so it is resolved before the settings lock is taken.
		var profileName = requested?.ProfileName ?? _turnService.ResolveProfileName(node);

		string? effectiveModelId;
		string? profileConfigDir;
		lock (_appSettings.SyncRoot)
		{
			effectiveModelId = requested?.ModelId == null ? ResolveEffectiveModel(node)
				: requested.ModelId == Constants.Remote.DefaultOptionId ? null : requested.ModelId;

			profileConfigDir = ResolveProfileConfigDir(profileName);
		}

		var modelInfo = string.IsNullOrEmpty(effectiveModelId)
			? null
			: _models.GetCachedModels().FirstOrDefault(m => m.Id == effectiveModelId);

		var effort = requested?.Effort;
		var effectiveEffort = string.IsNullOrEmpty(effort) || effort == Constants.Remote.DefaultOptionId ? null : effort;
		return new SessionCompactionOptionsModel(effectiveModelId, effectiveEffort, profileConfigDir, modelInfo);
	}

	private string? ResolveEffectiveModel(SessionNodeModel node)
	{
		if (!string.IsNullOrEmpty(node.ModelId))
			return node.ModelId;

		var directory = _appSettings.Settings.Tree.FirstOrDefault(d =>
			string.Equals(d.Path, node.WorkingDirectory, StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrEmpty(directory?.SelectedModelId))
			return directory.SelectedModelId;

		return string.IsNullOrEmpty(_appSettings.Settings.SelectedModelId) ? null : _appSettings.Settings.SelectedModelId;
	}

	/// <summary>Caller holds the settings lock. A name that matches no stored profile (the Default label, a deleted profile) means the Default profile.</summary>
	private string? ResolveProfileConfigDir(string? profileName)
	{
		if (string.IsNullOrEmpty(profileName))
			return null;

		IReadOnlyList<ClaudeProfileModel> profiles = _appSettings.Settings.Profiles;
		for (var i = 0; i < profiles.Count; i++)
		{
			if (string.Equals(profiles[i].DisplayName, profileName, StringComparison.OrdinalIgnoreCase))
				return _profiles.GetConfigDirForProfile(i + 1, profiles);
		}

		return null;
	}

	private async Task RunAsync(SessionNodeModel node, SessionCompactionOptionsModel options)
	{
		string? error = null;
		try
		{
			var result = await _compaction.CompactAsync(node, options, CancellationToken.None);
			if (result.Outcome != SessionCompactionOutcome.Compacted)
			{
				error = result.Outcome == SessionCompactionOutcome.Failed ? result.Error ?? result.SystemMessage : null;

				// An append never touches the existing history; it tells the web client why nothing changed.
				_fileService.AppendMessage(node.FileName, Constants.SessionFile.RoleSystem, result.SystemMessage);
			}
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Remote compaction failed for session {FileName}", node.FileName);
			error = ex.Message;
		}

		try
		{
			_fileService.NotifyTurnCompleted(node.FileName, cancelled: false, error);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "TurnCompleted handler failed after remote compaction of {FileName}", node.FileName);
		}
	}
}
