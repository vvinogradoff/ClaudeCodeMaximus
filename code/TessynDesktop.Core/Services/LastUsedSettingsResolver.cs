using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The model, effort and profile a session was last used with, worked out from its transcript exactly as the
/// desktop restores them when the session is opened (SessionViewModel.RestoreLastUsedSettings,
/// ModelSelectionController.RestoreFromEntry, ProfileController.RestoreFromEntry).
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class LastUsedSettingsResolver
{
	/// <summary>
	/// Model and effort come from the last ASSISTANT entry that recorded a model; the profile from the last USER entry
	/// that recorded one. An unknown model, an unknown effort, a renamed or deleted profile and a "[Sys] ..." label all
	/// fall back to "default".
	/// </summary>
	public static LastUsedSettingsDto Resolve(
		IReadOnlyList<SessionEntryModel> entries,
		IReadOnlyList<ClaudeModelInfo> models,
		IReadOnlyList<ClaudeProfileModel> profiles,
		string defaultProfileLabel)
	{
		var lastWithProfile = entries.LastOrDefault(e => e.Role == Constants.SessionFile.RoleUser && e.ProfileName != null);
		var lastWithModel   = entries.LastOrDefault(e => e.Role == Constants.SessionFile.RoleAssistant && e.ModelId != null);

		return new LastUsedSettingsDto(
			ResolveModel(lastWithModel?.ModelId, models),
			ResolveEffort(lastWithModel?.Effort),
			ResolveProfileId(lastWithProfile?.ProfileName, profiles, out var profileName, defaultProfileLabel),
			profileName);
	}

	private static string ResolveModel(string? modelId, IReadOnlyList<ClaudeModelInfo> models)
	{
		if (string.IsNullOrEmpty(modelId) || modelId == Constants.Remote.DefaultOptionId)
			return Constants.Remote.DefaultOptionId;

		return models.Any(m => m.Id == modelId) ? modelId : Constants.Remote.DefaultOptionId;
	}

	private static string ResolveEffort(string? effort)
	{
		if (string.IsNullOrEmpty(effort) || effort == Constants.Remote.DefaultOptionId)
			return Constants.Remote.DefaultOptionId;

		return Array.IndexOf(Constants.Remote.Efforts, effort) >= 0 ? effort : Constants.Remote.DefaultOptionId;
	}

	private static string ResolveProfileId(
		string? recordedName,
		IReadOnlyList<ClaudeProfileModel> profiles,
		out string profileName,
		string defaultProfileLabel)
	{
		if (recordedName != null)
		{
			var match = profiles.FirstOrDefault(p => string.Equals(p.DisplayName, recordedName, StringComparison.OrdinalIgnoreCase));
			if (match != null)
			{
				profileName = match.DisplayName;
				return match.ProfileId;
			}
		}

		profileName = defaultProfileLabel;
		return Constants.Remote.DefaultOptionId;
	}
}
