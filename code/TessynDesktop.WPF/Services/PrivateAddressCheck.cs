using System.Net;
using System.Net.Sockets;

namespace TessynDesktop.WPF.Services;

/// <summary>Tells loopback, private, link-local and similar addresses and host names from public ones, so a clicked or allowlisted link cannot make the client reach into the local network.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PrivateAddressCheck
{
	/// <summary>True for a host name or literal that may be fetched: not localhost, not a single-label name, not a private IP literal.</summary>
	public bool IsPublicHost(string host)
	{
		if (string.IsNullOrWhiteSpace(host))
			return false;

		var name = host.Trim().TrimEnd('.').ToLowerInvariant();
		if (name.StartsWith('[') && name.EndsWith(']'))
			name = name[1..^1];
		if (IPAddress.TryParse(name, out var address))
			return IsPublic(address);

		return name.Contains('.') && name != "localhost" && !name.EndsWith(".localhost", StringComparison.Ordinal) && !name.EndsWith(".local", StringComparison.Ordinal)
			&& !name.EndsWith(".internal", StringComparison.Ordinal) && !name.EndsWith(".lan", StringComparison.Ordinal);
	}

	/// <summary>True for an address on the public internet.</summary>
	public bool IsPublic(IPAddress address)
	{
		if (address.IsIPv4MappedToIPv6)
			address = address.MapToIPv4();

		if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
			return false;

		if (address.AddressFamily == AddressFamily.InterNetworkV6)
		{
			var bytes = address.GetAddressBytes();
			return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (bytes[0] & 0xFE) == 0xFC); // fc00::/7 unique local
		}

		var b = address.GetAddressBytes();
		return !(b[0] == 0 || b[0] == 10 || b[0] == 127 || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || (b[0] == 169 && b[1] == 254)
			|| (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 192 && b[1] == 0 && b[2] == 0) || b[0] >= 224);
	}
}
