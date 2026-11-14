using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ITdcpTokenStore" />
/// <remarks>Created by Claude</remarks>
public sealed class TdcpTokenStore : ITdcpTokenStore
{
	private static readonly ILogger _log = Log.ForContext<TdcpTokenStore>();

	public TdcpTokenStore() : this(GetDefaultPath())
	{
	}

	/// <summary>Constructor for testing — allows injecting a custom file path.</summary>
	public TdcpTokenStore(string path)
	{
		FilePath = path;
		var dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir))
			Directory.CreateDirectory(dir);
	}

	/// <inheritdoc />
	public string FilePath { get; }

	private static string GetDefaultPath()
	{
		var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		return Path.Combine(appData, Constants.AppDataFolderName, Constants.Remote.Discovery.TokenFileName);
	}

	/// <inheritdoc />
	public string? Load()
	{
		if (!File.Exists(FilePath))
			return null;

		try
		{
			var encrypted = File.ReadAllBytes(FilePath);
			var plaintext = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
			var token = Encoding.UTF8.GetString(plaintext);
			return string.IsNullOrEmpty(token) ? null : token;
		}
		catch (Exception ex)
		{
			// Corrupt file or another user's DPAPI context: treated as "no token"; the caller issues a new one.
			_log.Warning(ex, "TdcpTokenStore: failed to load/decrypt the TDCP token");
			return null;
		}
	}

	/// <inheritdoc />
	public void Save(string token)
	{
		var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), optionalEntropy: null, DataProtectionScope.CurrentUser);

		var tempPath = FilePath + ".tmp";
		File.WriteAllBytes(tempPath, encrypted);
		File.Move(tempPath, FilePath, overwrite: true);
	}
}
