using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ITurnOptionsResolver" />
/// <remarks>Created by Claude</remarks>
public sealed class TurnOptionsResolver : ITurnOptionsResolver
{
	private readonly IClaudeModelService _models;
	private readonly IAppSettingsService _appSettings;
	private readonly IProfileLabelService _labels;

	public TurnOptionsResolver(IClaudeModelService models, IAppSettingsService appSettings, IProfileLabelService labels)
	{
		_models      = models;
		_appSettings = appSettings;
		_labels      = labels;
	}

	/// <inheritdoc />
	public async Task<TurnOptionsModel?> ResolveAsync(JsonNode? @params)
	{
		var modelId   = TdcpParams.OptionalString(@params, "modelId");
		var effort    = TdcpParams.OptionalString(@params, "effort");
		var profileId = TdcpParams.OptionalString(@params, "profileId");

		if (modelId == null && effort == null && profileId == null)
			return null;

		if (modelId != null)
			ValidateModel(modelId);

		if (effort != null && Array.IndexOf(Constants.Remote.Efforts, effort) < 0)
			throw new TdcpInvalidParamsException($"Unknown effort '{effort}'");

		var profileName = profileId == null ? null : await ResolveProfileNameAsync(profileId);

		return new TurnOptionsModel(modelId, effort, profileName);
	}

	private void ValidateModel(string modelId)
	{
		if (modelId == Constants.Remote.DefaultOptionId)
			return;

		if (_models.GetCachedModels().All(m => m.Id != modelId))
			throw new TdcpInvalidParamsException($"Unknown model '{modelId}'");
	}

	private async Task<string> ResolveProfileNameAsync(string profileId)
	{
		if (profileId == Constants.Remote.DefaultOptionId)
			return await _labels.GetDefaultLabelAsync();

		lock (_appSettings.SyncRoot)
		{
			var profile = _appSettings.Settings.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
			if (profile != null)
				return profile.DisplayName;
		}

		throw new TdcpInvalidParamsException($"Unknown profile '{profileId}'");
	}
}
