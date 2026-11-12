using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>DPAPI-protected (CurrentUser) store of the relay link configuration (contract section 6).</summary>
/// <remarks>Created by Claude</remarks>
public interface IRelayConfigStore
{
	/// <summary>Returns the stored config, or null when missing, corrupt or protected for another user.</summary>
	RelayConfigModel? Load();

	/// <summary>Encrypts and persists <paramref name="config"/> atomically, overwriting any existing one.</summary>
	void Save(RelayConfigModel config);

	/// <summary>Removes the stored file, if any.</summary>
	void Delete();
}
