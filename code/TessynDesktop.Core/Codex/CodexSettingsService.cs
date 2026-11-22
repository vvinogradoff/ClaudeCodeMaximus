using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Codex;

/// <summary>
/// The daemon side of Settings > General: Codex (FR.33.17): the Codex path and its status line, the catalog profile, and the GPT tier table with
/// Reset to defaults (FR.33.14). The settings administration of phase 5 calls it; every change is saved at once.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexSettingsService
{
	private readonly IAppSettingsService _appSettings;
	private readonly CodexExecutableLocator _locator;

	public CodexSettingsService(IAppSettingsService appSettings, CodexExecutableLocator? locator = null)
	{
		_appSettings = appSettings;
		_locator     = locator ?? new CodexExecutableLocator();
	}

	/// <summary>Where Codex is found with the configured path (empty = find it), its version and the status line (FR.33.17).</summary>
	public async Task<CodexStatusModel> GetStatusAsync(CancellationToken cancellationToken = default)
	{
		var located = _locator.Locate(ConfiguredPath());
		Version? version = null;
		if (located.Found)
			version = await _locator.ReadVersionAsync(located.ExecutablePath!, cancellationToken).ConfigureAwait(false);

		return new CodexStatusModel(
			located.Found,
			located.ExecutablePath,
			located.DiscoveredPath,
			located.Source.ToString(),
			version?.ToString(3),
			Constants.Codex.TestedVersion,
			version is null ? null : _locator.VersionNote(version),
			_locator.StatusLine(located, version));
	}

	/// <summary>The configured Codex path; empty or null clears it (find it). New hosts use it.</summary>
	public void SetCodexPath(string? path)
	{
		lock (_appSettings.SyncRoot)
		{
			_appSettings.Settings.CodexPath = path?.Trim() ?? string.Empty;
			_appSettings.Save();
		}
	}

	/// <summary>The catalog profile (FR.33.2); null clears it (Codex Default, else the first signed-in one).</summary>
	public void SetCatalogProfile(string? profileId)
	{
		lock (_appSettings.SyncRoot)
		{
			_appSettings.Settings.CodexCatalogProfileId = string.IsNullOrWhiteSpace(profileId) ? null : profileId;
			_appSettings.Save();
		}
	}

	/// <summary>The GPT tier table in use, top row first: the stored one, or the defaults.</summary>
	public IReadOnlyList<CodexModelTierModel> GetModelTiers()
	{
		lock (_appSettings.SyncRoot)
			return _appSettings.Settings.CodexModelTiers?.Select(Copy).ToList() ?? Defaults();
	}

	/// <summary>Stores an edited table (it applies from the next tool call). Blank matches are dropped.</summary>
	public void SetModelTiers(IEnumerable<CodexModelTierModel> rows)
	{
		lock (_appSettings.SyncRoot)
		{
			_appSettings.Settings.CodexModelTiers = rows.Where(r => !string.IsNullOrWhiteSpace(r.Match))
				.Select(r => new CodexModelTierModel { Match = r.Match.Trim(), Tier = r.Tier }).ToList();
			_appSettings.Save();
		}
	}

	/// <summary>Reset to defaults: the stored table is dropped, so the defaults of this build apply.</summary>
	public void ResetModelTiers()
	{
		lock (_appSettings.SyncRoot)
		{
			_appSettings.Settings.CodexModelTiers = null;
			_appSettings.Save();
		}
	}

	private string? ConfiguredPath()
	{
		lock (_appSettings.SyncRoot)
			return string.IsNullOrWhiteSpace(_appSettings.Settings.CodexPath) ? null : _appSettings.Settings.CodexPath;
	}

	private List<CodexModelTierModel> Defaults() =>
		[.. Constants.Codex.Tiers.Defaults.Select(d => new CodexModelTierModel { Match = d.Match, Tier = d.Tier })];

	private CodexModelTierModel Copy(CodexModelTierModel row) => new() { Match = row.Match, Tier = row.Tier };
}
