using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// DPAPI-protected (CurrentUser scope) single-file bot token store (FR.21.6). Mirrors
/// <see cref="McpTokenStore"/>'s encryption approach but persists exactly one secret, never
/// written to appsettings.json.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TelegramTokenStore : ITelegramTokenStore
{
	private static readonly ILogger _log = Log.ForContext<TelegramTokenStore>();

	private readonly string _path;

	public TelegramTokenStore() : this(GetDefaultPath())
	{
	}

	/// <summary>Constructor for testing — allows injecting a custom file path.</summary>
	public TelegramTokenStore(string path)
	{
		_path = path;
		var dir = Path.GetDirectoryName(_path);
		if (!string.IsNullOrEmpty(dir))
			Directory.CreateDirectory(dir);
	}

	private static string GetDefaultPath()
	{
		var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		return Path.Combine(appData, Constants.AppDataFolderName, Constants.Telegram.TokenFileName);
	}

	public string? Load()
	{
		if (!File.Exists(_path))
			return null;

		try
		{
			var encrypted = File.ReadAllBytes(_path);
			var plaintext = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
			return Encoding.UTF8.GetString(plaintext);
		}
		catch (Exception ex)
		{
			// Corrupt file, or DPAPI-decrypted-by-a-different-user-context — treat as "not configured"
			// rather than throwing, since callers only ever want a token or null.
			_log.Warning(ex, "TelegramTokenStore: failed to load/decrypt bot token");
			return null;
		}
	}

	public void Save(string token)
	{
		var plaintext = Encoding.UTF8.GetBytes(token);
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
