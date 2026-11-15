using System;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ITurnSettingsResolver" />
/// <remarks>
/// The model rules repeat those of <see cref="SessionTurnService"/> (per-turn, node, directory, app); the profile comes from the shared
/// <see cref="IProfileResolver"/> (ISSUE.013). Created by Claude.
/// </remarks>
public sealed class TurnSettingsResolver : ITurnSettingsResolver
{
	private readonly IAppSettingsService _appSettings;
	private readonly ISessionTurnService _turns;
	private readonly IClaudeProfileService _profiles;
	private readonly IClaudeModelService _models;
	private readonly IProfileLabelService _labels;
	private readonly IProfileResolver _resolver;

	public TurnSettingsResolver(
		IAppSettingsService appSettings,
		ISessionTurnService turns,
		IClaudeProfileService profiles,
		IClaudeModelService models,
		IProfileLabelService labels,
		IProfileResolver? resolver = null)
	{
		_appSettings = appSettings;
		_turns       = turns;
		_profiles    = profiles;
		_models      = models;
		_labels      = labels;
		_resolver    = resolver ?? new ProfileResolver(appSettings, profiles);
	}

	/// <inheritdoc />
	public async Task<TurnSettings> ResolveAsync(SessionNodeModel node, TurnOptionsModel? options)
	{
		// ISSUE.013: read-only. A switch queued for this session is applied only under its turn lock (TurnEngine), never here: the
		// session may still be running its previous turn. The settings already name the queued target so the turn launches on it.
		// A pinned profile that does not resolve is an error, never Default.
		var pending = options?.ProfileName == null && !string.IsNullOrEmpty(node.PendingProfileId) ? _resolver.FindById(node.PendingProfileId) : null;
		var profile = pending ?? _resolver.Resolve(node, _turns.ResolveProfileName(node), options?.ProfileName);
		if (!profile.IsResolved)
			throw new TdcpException(profile.Error!);

		var stampedProfileName = profile.IsDefault ? await _labels.GetDefaultLabelAsync() : profile.DisplayName;
		var profileConfigDir   = profile.ConfigDir;

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
}
