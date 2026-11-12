using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Stores the relay config as <c>ProtectedData.Protect(UTF8(JSON), optionalEntropy: null, CurrentUser)</c> in
/// <c>%AppData%\TessynDesktop\web-relay.dat</c>. The format is fixed by contract section 6 because the file may be
/// provisioned from PowerShell. Cloned from <see cref="TelegramTokenStore"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class RelayConfigStore : IRelayConfigStore
{
	private static readonly ILogger _log = Log.ForContext<RelayConfigStore>();

	private readonly string _path;

	public RelayConfigStore() : this(GetDefaultPath())
	{
	}

	/// <summary>Constructor for testing — allows injecting a custom file path.</summary>
	public RelayConfigStore(string path)
	{
		_path = path;
		var dir = Path.GetDirectoryName(_path);
		if (!string.IsNullOrEmpty(dir))
			Directory.CreateDirectory(dir);
	}

	private static string GetDefaultPath()
	{
		var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		return Path.Combine(appData, Constants.AppDataFolderName, Constants.Remote.Link.ConfigFileName);
	}

	public RelayConfigModel? Load()
	{
		if (!File.Exists(_path))
			return null;

		try
		{
			var encrypted = File.ReadAllBytes(_path);
			var plaintext = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
			return JsonSerializer.Deserialize<RelayConfigModel>(Encoding.UTF8.GetString(plaintext), TdcpJsonOptions.Default);
		}
		catch (Exception ex)
		{
			// Corrupt file, other user's DPAPI context or bad JSON: treated as "not configured".
			_log.Warning(ex, "RelayConfigStore: failed to load/decrypt the relay config");
			return null;
		}
	}

	public void Save(RelayConfigModel config)
	{
		var plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config, TdcpJsonOptions.Default));
		var encrypted = ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);

		var tempPath = _path + ".tmp";
		File.WriteAllBytes(tempPath, encrypted);
		File.Move(tempPath, _path, overwrite: true);
	}

	public void Delete()
	{
		if (File.Exists(_path))
			File.Delete(_path);
	}
}
