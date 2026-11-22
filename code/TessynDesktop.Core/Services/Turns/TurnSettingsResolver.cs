using System;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ITurnSettingsResolver" />
/// <remarks>
/// The model and effort come from the shared <see cref="ISessionModelSettings"/> (FR.12.35), as for <see cref="SessionTurnService"/>; the profile
/// comes from the shared <see cref="IProfileResolver"/> (ISSUE.013). Created by Claude.
/// </remarks>
public sealed class TurnSettingsResolver : ITurnSettingsResolver
{
	private readonly IAppSettingsService _appSettings;
	private readonly ISessionTurnService _turns;
	private readonly IClaudeProfileService _profiles;
	private readonly IClaudeModelService _models;
	private readonly IProfileLabelService _labels;
	private readonly IProfileResolver _resolver;
	private readonly ISessionModelSettings _modelSettings;
	private readonly ISessionEngineRouter? _engines;
	private readonly TessynDesktop.Codex.ICodexProfileResolver? _codexProfiles;
	private readonly TessynDesktop.Codex.CodexProfileNaming _codexNaming = new();

	public TurnSettingsResolver(
		IAppSettingsService appSettings,
		ISessionTurnService turns,
		IClaudeProfileService profiles,
		IClaudeModelService models,
		IProfileLabelService labels,
		IProfileResolver? resolver = null,
		ISessionModelSettings? modelSettings = null,
		ISessionEngineRouter? engines = null,
		TessynDesktop.Codex.ICodexProfileResolver? codexProfiles = null)
	{
		_engines       = engines;
		_codexProfiles = codexProfiles;
		_appSettings = appSettings;
		_turns       = turns;
		_profiles    = profiles;
		_models      = models;
		_labels      = labels;
		_resolver    = resolver ?? new ProfileResolver(appSettings, profiles);
		_modelSettings = modelSettings ?? new SessionModelSettings(appSettings, models);
	}

	/// <inheritdoc />
	public async Task<TurnSettings> ResolveAsync(SessionNodeModel node, TurnOptionsModel? options)
	{
		// FR.33.4, FR.33.5: a turn on a GPT model runs on Codex, with a ChatGPT profile (its pin, exact, else the inherit rule).
		if (_engines != null && _codexProfiles != null)
		{
			var gptTurn = _modelSettings.ResolveTurn(node, options);
			if (string.Equals(_engines.Peek(node, gptTurn.ModelId).EngineId, Constants.SessionEngines.Codex, StringComparison.OrdinalIgnoreCase))
			{
				// FR.12.28, FR.33.5: a ChatGPT switch queued while the session ran is the profile of its next turn (it is applied under the lock),
				// as the Claude branch below resolves the queued target.
				var queuedCodex = options?.ProfileName == null && _codexNaming.IsCodexProfileId(node.PendingProfileId) ? node.PendingProfileId : null;
				var choice  = _codexProfiles.ResolveForTurn(node, options?.ProfileName ?? queuedCodex);
				if (!choice.IsResolved)
					throw new TdcpException(choice.Error!);

				var gptDirectory = _appSettings.Settings.Tree.FirstOrDefault(d =>
					string.Equals(d.Path, node.WorkingDirectory, StringComparison.OrdinalIgnoreCase));
				return new TurnSettings(gptTurn.ModelId, gptTurn.Effort, null, choice.DisplayName, null, false, Chrome: false, null, gptDirectory, choice.Profile);
			}
		}

		// ISSUE.013: read-only. A switch queued for this session is applied only under its turn lock (TurnEngine), never here: the
		// session may still be running its previous turn. The settings already name the queued target so the turn launches on it.
		// A pinned profile that does not resolve is an error, never Default.
		var pending = options?.ProfileName == null && !string.IsNullOrEmpty(node.PendingProfileId) ? _resolver.FindById(node.PendingProfileId) : null;
		var profile = pending ?? _resolver.Resolve(node, _turns.ResolveProfileName(node), options?.ProfileName);
		if (!profile.IsResolved)
			throw new TdcpException(profile.Error!);

		var stampedProfileName = profile.IsDefault ? await _labels.GetDefaultLabelAsync() : profile.DisplayName;
		var profileConfigDir   = profile.ConfigDir;

		var turn      = _modelSettings.ResolveTurn(node, options);
		var modelId   = turn.ModelId;
		var effort    = turn.Effort;
		var modelInfo = string.IsNullOrEmpty(modelId)
			? null
			: _models.GetCachedModels().FirstOrDefault(m => m.Id == modelId);

		var ollamaBaseUrl = modelInfo?.Provider == ModelProvider.Ollama ? _appSettings.Settings.OllamaBaseUrl : null;
		var disableTools  = modelInfo is { Provider: ModelProvider.Ollama, SupportsTools: false };
		var directory     = _appSettings.Settings.Tree.FirstOrDefault(d =>
			string.Equals(d.Path, node.WorkingDirectory, StringComparison.OrdinalIgnoreCase));

		return new TurnSettings(modelId, effort, profileConfigDir, stampedProfileName, ollamaBaseUrl, disableTools, Chrome: false, modelInfo, directory);
	}
}
