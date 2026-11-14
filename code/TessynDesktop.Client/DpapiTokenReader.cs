using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace TessynDesktop.Client;

/// <summary>
/// Reads the local token file with exactly the scheme of the daemon's <c>TdcpTokenStore</c>: the file holds the UTF-8 token
/// protected by DPAPI for the current Windows user, with no extra entropy.
/// </summary>
/// <remarks>Created by Claude</remarks>
[SupportedOSPlatform("windows")]
internal static class DpapiTokenReader
{
	/// <summary>The token, or null when the file is missing, empty, corrupt or protected for another user.</summary>
	public static string? Read(string path)
	{
		if (!File.Exists(path))
			return null;

		try
		{
			var encrypted = File.ReadAllBytes(path);
			var plaintext = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
			var token = Encoding.UTF8.GetString(plaintext);
			return string.IsNullOrEmpty(token) ? null : token;
		}
		catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}
}
