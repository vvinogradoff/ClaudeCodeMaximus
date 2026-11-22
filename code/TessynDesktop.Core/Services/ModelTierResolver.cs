using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IModelTierResolver" />
/// <remarks>
/// The table is read on every call, so an edit applies from the next tool call (FR.33.14). A GPT-looking id that is not in the catalog also uses
/// the table, so a stale GPT id is never tier 0. Created by Claude.
/// </remarks>
public sealed class ModelTierResolver : IModelTierResolver
{
	private readonly IAppSettingsService _appSettings;
	private readonly ISessionModelCatalog? _catalog;

	public ModelTierResolver(IAppSettingsService appSettings, ISessionModelCatalog? catalog = null)
	{
		_appSettings = appSettings;
		_catalog     = catalog;
	}

	/// <inheritdoc />
	public int GetTier(string? modelId)
	{
		if (_catalog is null || !(_catalog.IsGpt(modelId) || _catalog.LooksLikeGpt(modelId)))
			return ModelTierService.GetTier(modelId);

		foreach (var row in Table())
		{
			if (!string.IsNullOrEmpty(row.Match) && modelId!.Contains(row.Match, StringComparison.OrdinalIgnoreCase))
				return row.Tier;
		}

		return Constants.Codex.Tiers.Unmatched;
	}

	/// <inheritdoc />
	public bool IsAllowed(string? callerModelId, string? requestedModelId) => GetTier(callerModelId) >= GetTier(requestedModelId);

	/// <inheritdoc />
	public string TierName(int tier) => ModelTierService.TierName(tier);

	private IReadOnlyList<CodexModelTierModel> Table()
	{
		lock (_appSettings.SyncRoot)
		{
			var stored = _appSettings.Settings.CodexModelTiers;
			return stored is null
				? Constants.Codex.Tiers.Defaults.Select(d => new CodexModelTierModel { Match = d.Match, Tier = d.Tier }).ToList()
				: stored.ToList();
		}
	}
}
