namespace TessynDesktop.Services;

/// <summary>
/// DPAPI-protected (CurrentUser scope) single-file store for the local TDCP token, kept out of appsettings.json.
/// Same pattern as <see cref="ITelegramTokenStore"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ITdcpTokenStore
{
	/// <summary>Full path of the token file (published in <c>daemon.json</c> as <c>tokenFile</c>).</summary>
	string FilePath { get; }

	/// <summary>Returns the stored token, or null if none is stored or it cannot be decrypted.</summary>
	string? Load();

	/// <summary>Encrypts and persists the token, overwriting any existing one.</summary>
	void Save(string token);
}
