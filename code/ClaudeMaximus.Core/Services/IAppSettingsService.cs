using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <summary>
/// Loads and saves application settings to appsettings.json atomically.
/// Single source of truth for all persistent application state.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IAppSettingsService
{
	AppSettingsModel Settings { get; }
	void Load();
	void Save();
}
