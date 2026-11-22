using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Codex;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionEngineAvailability" />
/// <remarks>
/// The Claude engine is always listed: its CLI is the configured <c>ClaudePath</c>, which Tessyn does not discover. The Codex engine is listed when
/// the locator finds a usable native <c>codex.exe</c> with the configured <c>CodexPath</c> (FR.33.17), the same rule the host pool launches with.
/// Created by Claude.
/// </remarks>
public sealed class SessionEngineAvailability : ISessionEngineAvailability
{
	private readonly IEnumerable<ISessionEngine> _engines;
	private readonly IAppSettingsService _appSettings;
	private readonly CodexExecutableLocator _locator;

	public SessionEngineAvailability(IEnumerable<ISessionEngine> engines, IAppSettingsService appSettings, CodexExecutableLocator? locator = null)
	{
		_engines     = engines;
		_appSettings = appSettings;
		_locator     = locator ?? new CodexExecutableLocator();
	}

	/// <inheritdoc />
	public IReadOnlyList<string> GetAvailableEngines() =>
		[.. _engines.Select(e => e.EngineId).Distinct().Where(IsAvailable)];

	private bool IsAvailable(string engineId) =>
		engineId != Constants.SessionEngines.Codex || _locator.Locate(ConfiguredCodexPath()).Found;

	private string? ConfiguredCodexPath()
	{
		lock (_appSettings.SyncRoot)
			return string.IsNullOrWhiteSpace(_appSettings.Settings.CodexPath) ? null : _appSettings.Settings.CodexPath.Trim();
	}
}
