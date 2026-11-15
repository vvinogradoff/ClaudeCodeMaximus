using System.Net;
using System.Net.Sockets;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Tells loopback, private, link-local, documentation, benchmarking, transition and similar addresses and host names from public ones, so a
/// clicked or allowlisted link cannot make the client reach into the local network. IPv6 addresses that embed an IPv4 address (mapped,
/// compatible, NAT64, 6to4) are judged by the embedded address; Teredo is refused.
/// </summary>
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

		return address.AddressFamily == AddressFamily.InterNetworkV6 ? IsPublicV6(address) : IsPublicV4(address.GetAddressBytes());
	}

	private bool IsPublicV6(IPAddress address)
	{
		var b = address.GetAddressBytes();
		if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC) // fc00::/7 unique local
			return false;

		if (address.IsIPv6Teredo || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0 && b[3] == 0))   // 2001::/32 Teredo: tunnels to an arbitrary server
			return false;

		if (b.Take(12).All(x => x == 0))                                                           // ::a.b.c.d IPv4-compatible
			return IsPublicV4(b[12..]);

		if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B)                          // 64:ff9b::/96 NAT64 (and the local-use 64:ff9b:1::/48)
			return b[4..12].All(x => x == 0) && IsPublicV4(b[12..]);

		if (b[0] == 0x20 && b[1] == 0x02)                                                          // 2002::/16 6to4: the IPv4 address is bytes 2..5
			return IsPublicV4(b[2..6]);

		return true;
	}

	private static bool IsPublicV4(byte[] b) =>
		!(b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224
			|| (b[0] == 100 && b[1] >= 64 && b[1] <= 127)                       // 100.64.0.0/10 CGNAT
			|| (b[0] == 169 && b[1] == 254)                                     // link-local
			|| (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
			|| (b[0] == 192 && b[1] == 168)
			|| (b[0] == 192 && b[1] == 0 && (b[2] == 0 || b[2] == 2))           // 192.0.0.0/24, 192.0.2.0/24 documentation
			|| (b[0] == 198 && (b[1] == 18 || b[1] == 19))                      // 198.18.0.0/15 benchmarking
			|| (b[0] == 198 && b[1] == 51 && b[2] == 100)                       // 198.51.100.0/24 documentation
			|| (b[0] == 203 && b[1] == 0 && b[2] == 113));                      // 203.0.113.0/24 documentation
}
