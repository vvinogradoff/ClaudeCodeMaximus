using System;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ITurnSettingsResolver" />
/// <remarks>
/// The same rules as the private resolution of <see cref="SessionTurnService"/> (model: per-turn, node, directory, app; profile: per-turn,
/// pinned or last recorded, Default), repeated here because that service's constructor would have to change to share them - a consolidation
/// for the Architect to approve. Created by Claude.
/// </remarks>
public sealed class TurnSettingsResolver : ITurnSettingsResolver
{
	private readonly IAppSettingsService _appSettings;
	private readonly ISessionTurnService _turns;
	private readonly IClaudeProfileService _profiles;
	private readonly IClaudeModelService _models;
	private readonly IProfileLabelService _labels;

	public TurnSettingsResolver(
		IAppSettingsService appSettings,
		ISessionTurnService turns,
		IClaudeProfileService profiles,
		IClaudeModelService models,
		IProfileLabelService labels)
	{
		_appSettings = appSettings;
		_turns       = turns;
		_profiles    = profiles;
		_models      = models;
		_labels      = labels;
	}

	/// <inheritdoc />
	public async Task<TurnSettings> ResolveAsync(SessionNodeModel node, TurnOptionsModel? options)
	{
		var requestedProfileName = options?.ProfileName ?? _turns.ResolveProfileName(node);
		var stampedProfileName   = await ResolveStampedProfileNameAsync(requestedProfileName);
		var profileConfigDir     = ResolveProfileConfigDir(requestedProfileName);

		var modelId   = ResolveTurnModel(node, options);
		var effort    = NormalizeEffort(options?.Effort);
		var modelInfo = string.IsNullOrEmpty(modelId)
			? null
			: _models.GetCachedModels().FirstOrDefault(m => m.Id == modelId);

		var ollamaBaseUrl = modelInfo?.Provider == ModelProvider.Ollama ? _appSettings.Settings.OllamaBaseUrl : null;
		var disableTools  = modelInfo is { Provider: ModelProvider.Ollama, SupportsTools: false };
		var directory     = _appSettings.Settings.Tree.FirstOrDefault(d =>
			string.Equals(d.Path, node.WorkingDirectory, StringComparison.OrdinalIgnoreCase));

		return new TurnSettings(modelId, effort, profileConfigDir, stampedProfileName, ollamaBaseUrl, disableTools, Chrome: false, modelInfo, directory);
	}

	/// <summary>The per-turn choice (<c>"default"</c> = no --model flag), else node, directory, app-level model; null when nothing is configured.</summary>
	private string? ResolveTurnModel(SessionNodeModel node, TurnOptionsModel? options)
	{
		var requested = options?.ModelId;
		if (string.IsNullOrEmpty(requested))
			return ResolveEffectiveModel(node);

		return string.Equals(requested, Constants.Remote.DefaultOptionId, StringComparison.OrdinalIgnoreCase) ? null : requested;
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

	private static string? NormalizeEffort(string? effort) =>
		string.IsNullOrWhiteSpace(effort) || string.Equals(effort, Constants.Remote.DefaultOptionId, StringComparison.OrdinalIgnoreCase)
			? null
			: effort;

	/// <summary>The stored profile's display name, otherwise the Default profile's "[Sys] ..." label (what the desktop stamps for Default).</summary>
	private async Task<string> ResolveStampedProfileNameAsync(string? profileName)
	{
		if (!string.IsNullOrEmpty(profileName))
		{
			var stored = FindStoredProfile(profileName);
			if (stored != null)
				return stored.DisplayName;
		}

		return await _labels.GetDefaultLabelAsync();
	}

	private ClaudeProfileModel? FindStoredProfile(string profileName)
	{
		lock (_appSettings.SyncRoot)
		{
			return _appSettings.Settings.Profiles.FirstOrDefault(p => string.Equals(p.DisplayName, profileName, StringComparison.OrdinalIgnoreCase));
		}
	}

	/// <summary>The credential store of the named profile; null (= Default) for no name or a profile that no longer exists.</summary>
	private string? ResolveProfileConfigDir(string? name)
	{
		if (string.IsNullOrEmpty(name))
			return null;

		var profiles = _appSettings.Settings.Profiles;
		for (var i = 0; i < profiles.Count; i++)
		{
			if (string.Equals(profiles[i].DisplayName, name, StringComparison.OrdinalIgnoreCase))
				return _profiles.GetConfigDirForProfile(i + 1, profiles);
		}

		return null;
	}
}
