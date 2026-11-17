using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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
		return Path.Combine(Constants.DataRoot, Constants.Remote.Discovery.TokenFileName);
	}

	/// <inheritdoc />
	public string? Load()
	{
		if (!File.Exists(FilePath))
			return null;

		byte[] encrypted = ReadWithRetry();

		try
		{
			var plaintext = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
			var token = Encoding.UTF8.GetString(plaintext);
			return string.IsNullOrEmpty(token) ? null : token;
		}
		catch (CryptographicException ex)
		{
			// Corrupt/malformed file or another user's DPAPI context: treated as "no token"; the caller issues a new one.
			_log.Warning(ex, "TdcpTokenStore: the TDCP token file cannot be decrypted");
			return null;
		}
		catch (ArgumentException ex)
		{
			_log.Warning(ex, "TdcpTokenStore: the TDCP token file is malformed");
			return null;
		}
	}

	/// <summary>
	/// Reads the file, retrying briefly on <see cref="IOException"/> (antivirus/indexer sharing violations). Persistent failure
	/// throws, so the caller never mistakes an unreadable file for "no token" and overwrites (rotates) the good one (review A3).
	/// </summary>
	private byte[] ReadWithRetry()
	{
		const int attempts = 5;
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				return File.ReadAllBytes(FilePath);
			}
			catch (IOException ex) when (attempt < attempts)
			{
				_log.Debug(ex, "TdcpTokenStore: token file busy, retrying ({Attempt}/{Attempts})", attempt, attempts);
				Thread.Sleep(50 * attempt);
			}
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
