namespace TessynDesktop.Models;

/// <summary>The relay link configuration persisted DPAPI-protected in web-relay.dat (contract section 6).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class RelayConfigModel
{
	/// <summary>Whether the desktop dials out to the web server at all.</summary>
	public bool Enabled { get; set; }

	/// <summary>Base URL of the web server, e.g. https://tessyn.vvinogradoff.com.</summary>
	public string Url { get; set; } = string.Empty;

	/// <summary>The link token presented as the Bearer credential.</summary>
	public string Token { get; set; } = string.Empty;
}
