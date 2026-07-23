using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public sealed class AppSettingsService : IAppSettingsService
{
	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	private readonly string _settingsFilePath;

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
		if (!File.Exists(_settingsFilePath))
		{
			EnsureSessionFilesRoot();
			return;
		}

		var json = File.ReadAllText(_settingsFilePath);
		Settings = JsonSerializer.Deserialize<AppSettingsModel>(json, SerializerOptions) ?? new AppSettingsModel();
		EnsureSessionFilesRoot();
	}

	public void Save()
	{
		var json = JsonSerializer.Serialize(Settings, SerializerOptions);
		var tempPath = _settingsFilePath + ".tmp";
		File.WriteAllText(tempPath, json);
		File.Move(tempPath, _settingsFilePath, overwrite: true);
	}

	private void EnsureSessionFilesRoot()
	{
		if (!string.IsNullOrEmpty(Settings.SessionFilesRoot))
			Directory.CreateDirectory(Settings.SessionFilesRoot);
	}
}
