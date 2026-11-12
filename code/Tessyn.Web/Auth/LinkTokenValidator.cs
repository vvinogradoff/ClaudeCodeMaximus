using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Tessyn.Web.Options;

namespace Tessyn.Web.Auth;

/// <summary>Checks the Bearer token of daemon link requests against the configured SHA-256 hex.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class LinkTokenValidator
{
	private readonly IOptions<TessynWebOptions> _options;

	public LinkTokenValidator(IOptions<TessynWebOptions> options)
	{
		_options = options;
	}

	public bool IsAuthorized(HttpRequest request)
	{
		var header = request.Headers.Authorization.ToString();
		if (!header.StartsWith(Constants.Headers.BearerPrefix, StringComparison.OrdinalIgnoreCase))
			return false;

		var presented = header[Constants.Headers.BearerPrefix.Length..].Trim();
		var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));

		byte[] expected;
		try
		{
			expected = Convert.FromHexString(_options.Value.Link.TokenSha256);
		}
		catch (FormatException)
		{
			return false;
		}

		return CryptographicOperations.FixedTimeEquals(presentedHash, expected);
	}
}
