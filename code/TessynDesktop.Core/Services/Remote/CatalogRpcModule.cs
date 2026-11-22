using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Remote <c>catalog.get</c>: the models, efforts and profiles a turn can be sent with (contract section 3.20). FR.33.2 / tdcp-spec 10.A14: with the
/// merged catalog every model carries its engine and its own efforts, the GPT models follow the Anthropic ones, and the signed-in ChatGPT profiles
/// follow the Claude ones; without it (an older wiring) the Claude list is sent as before, each entry tagged <c>claude</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CatalogRpcModule : IRemoteRpcModule
{
	private readonly IClaudeModelService _models;
	private readonly IAppSettingsService _appSettings;
	private readonly IProfileLabelService _labels;
	private readonly ISessionModelCatalog? _sessionModels;
	private readonly ICodexProfileService? _codexProfiles;
	private readonly ISessionEngineAvailability? _engines;

	public CatalogRpcModule(IClaudeModelService models, IAppSettingsService appSettings, IProfileLabelService labels,
		ISessionModelCatalog? sessionModels = null, ICodexProfileService? codexProfiles = null, ISessionEngineAvailability? engines = null)
	{
		_models        = models;
		_appSettings   = appSettings;
		_labels        = labels;
		_sessionModels = sessionModels;
		_codexProfiles = codexProfiles;
		_engines       = engines;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry) =>
		registry.Add(Constants.Remote.Methods.CatalogGet, async (_, _) => await GetAsync(), remoteCallable: true);

	private async Task<object?> GetAsync()
	{
		var claudeEfforts = ClaudeEfforts();
		var models = new List<ModelOptionDto> { new(Constants.Remote.DefaultOptionId, "Default", null, RemoteProtocol.Engines.Claude, claudeEfforts) };
		if (_sessionModels != null)
			models.AddRange(_sessionModels.GetModels().Select(ToDto));
		else
			models.AddRange(_models.GetCachedModels().Select(m => new ModelOptionDto(m.Id, m.DisplayName, m.Provider, RemoteProtocol.Engines.Claude, claudeEfforts)));

		var profiles = new List<ProfileOptionDto> { new(Constants.Remote.DefaultOptionId, await _labels.GetDefaultLabelAsync()) };
		profiles.AddRange(StoredProfiles().Select(p => new ProfileOptionDto(p.ProfileId, p.DisplayName)));
		if (_codexProfiles != null)
			profiles.AddRange(_codexProfiles.List().Where(p => p.IsKnownSignedIn)
				.Select(p => new ProfileOptionDto(p.ProfileId, p.DisplayName, RemoteProtocol.Engines.Codex, p.PlanType)));

		return new CatalogResult(models, [.. Constants.Remote.Efforts], profiles, MediaPreviewHosts(), GptHint());
	}

	private static ModelOptionDto ToDto(SessionModelEntryModel model) =>
		new(model.Id, model.DisplayName, model.Provider, model.EngineId,
			[.. model.Efforts.Select(e => new ModelEffortDto(e.Effort, e.Description))],
			model.IsDefault ? true : null,
			model.Upgrade);

	/// <summary>The Claude list without <c>default</c>: what the "default" entry, the Anthropic models and the Ollama models offer.</summary>
	private static ModelEffortDto[] ClaudeEfforts() =>
		[.. Constants.Remote.Efforts.Where(e => e != Constants.Remote.DefaultOptionId).Select(e => new ModelEffortDto(e))];

	/// <summary>FR.33.2's disabled picker line; only when Codex can run here (else signing in would not help).</summary>
	private string? GptHint()
	{
		if (_sessionModels?.GptHintLine is not { } hint)
			return null;

		return _engines == null || _engines.GetAvailableEngines().Contains(RemoteProtocol.Engines.Codex) ? hint : null;
	}

	private string[] MediaPreviewHosts()
	{
		lock (_appSettings.SyncRoot)
			return [.. _appSettings.Settings.MediaPreviewHosts.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim().ToLowerInvariant())];
	}

	private ClaudeProfileModel[] StoredProfiles()
	{
		lock (_appSettings.SyncRoot)
			return [.. _appSettings.Settings.Profiles];
	}
}
