using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class AppSettingsService : IAppSettingsService
{
	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	private readonly string _settingsFilePath;

	/// <summary>
	/// Guards <see cref="Save"/>/<see cref="Load"/> and (via <see cref="SyncRoot"/>) every in-memory
	/// mutation of the shared <see cref="Settings"/> graph made from a background/worker thread —
	/// see ISSUE concurrent-settings-save-lost-update. Without this, two concurrent Saves could race
	/// (a Save that serialized a stale snapshot landing last and silently overwriting a newer one,
	/// e.g. one that had just added an orchestrated sub-agent session node).
	/// </summary>
	private readonly object _gate = new();

	public object SyncRoot => _gate;

	public AppSettingsModel Settings { get; private set; } = new();

	public AppSettingsService() : this(GetDefaultAppDataDir())
	{
	}

	/// <summary>Constructor for testing — allows injecting a custom base directory.</summary>
	public AppSettingsService(string baseDirectory)
	{
		Directory.CreateDirectory(baseDirectory);
		_settingsFilePath = Path.Combine(baseDirectory, Constants.SettingsFileName);

		if (string.IsNullOrEmpty(Settings.SessionFilesRoot))
			Settings.SessionFilesRoot = Path.Combine(baseDirectory, Constants.DefaultSessionsFolderName);
	}

	private static string GetDefaultAppDataDir()
	{
		var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		return Path.Combine(appData, Constants.AppDataFolderName);
	}

	public void Load()
	{
		lock (_gate)
		{
			if (!File.Exists(_settingsFilePath))
			{
				EnsureSessionFilesRoot();
				return;
			}

			var json = File.ReadAllText(_settingsFilePath);
			Settings = JsonSerializer.Deserialize<AppSettingsModel>(json, SerializerOptions) ?? new AppSettingsModel();
			EnsureSessionFilesRoot();
		}
	}

	public void Save()
	{
		lock (_gate)
		{
			var json = JsonSerializer.Serialize(Settings, SerializerOptions);
			var tempPath = _settingsFilePath + ".tmp";
			File.WriteAllText(tempPath, json);
			File.Move(tempPath, _settingsFilePath, overwrite: true);
		}
	}

	private void EnsureSessionFilesRoot()
	{
		if (!string.IsNullOrEmpty(Settings.SessionFilesRoot))
			Directory.CreateDirectory(Settings.SessionFilesRoot);
	}
}
