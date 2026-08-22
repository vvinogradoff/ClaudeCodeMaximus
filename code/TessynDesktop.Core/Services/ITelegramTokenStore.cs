namespace TessynDesktop.Services;

/// <summary>
/// DPAPI-protected (CurrentUser scope) single-file store for the Telegram bot token (FR.21.6).
/// Unlike <see cref="IMcpTokenStore"/> this holds exactly one secret — there is one bot — so it
/// has no server-id keying.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ITelegramTokenStore
{
	/// <summary>Returns the stored bot token, or null if none is stored or it cannot be decrypted.</summary>
	string? Load();

	/// <summary>Encrypts and persists the bot token, overwriting any existing one.</summary>
	void Save(string token);

	/// <summary>Removes the stored token file, if any.</summary>
	void Delete();
}
