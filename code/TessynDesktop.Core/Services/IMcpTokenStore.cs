using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// DPAPI-protected, per-server OAuth token cache for the External MCP Server Registry (FR.20.5).
/// Never stores tokens in appsettings.json.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IMcpTokenStore
{
	/// <summary>Reads and decrypts the stored token record for a server, or null if none exists or it fails to decrypt.</summary>
	McpTokenRecord? Load(string serverId);

	/// <summary>Encrypts and persists a token record for a server, overwriting any existing one.</summary>
	void Save(string serverId, McpTokenRecord record);

	/// <summary>Removes a server's stored token record, if any.</summary>
	void Delete(string serverId);
}
