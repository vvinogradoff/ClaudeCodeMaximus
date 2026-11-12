using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Tessyn.Web.Options;

namespace Tessyn.Web.Auth;

/// <summary>Builds and validates the login ticket's claims: a security stamp (password hash + epoch) and an absolute expiry.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AuthSessionService
{
	private readonly IOptions<TessynWebOptions> _options;
	private readonly AuthEpochStore _epoch;
	private readonly TimeProvider _time;

	public AuthSessionService(IOptions<TessynWebOptions> options, AuthEpochStore epoch, TimeProvider time)
	{
		_options = options;
		_epoch = epoch;
		_time = time;
	}

	public ClaimsPrincipal CreatePrincipal(string email)
	{
		var auth = _options.Value.Auth;
		var absoluteExpiry = _time.GetUtcNow().AddDays(auth.AbsoluteDays).ToUnixTimeSeconds();
		var identity = new ClaimsIdentity(
			[
				new Claim(ClaimTypes.Name, email),
				new Claim(ClaimTypes.Email, email),
				new Claim(Constants.Auth.StampClaim, CurrentStamp()),
				new Claim(Constants.Auth.AbsoluteExpiryClaim, absoluteExpiry.ToString(CultureInfo.InvariantCulture)),
			],
			Constants.Auth.CookieScheme);
		return new ClaimsPrincipal(identity);
	}

	/// <summary>False when the stamp no longer matches (logout / password change) or the absolute lifetime is over.</summary>
	public bool IsValid(ClaimsPrincipal? principal)
	{
		var stamp = principal?.FindFirstValue(Constants.Auth.StampClaim);
		var expiry = principal?.FindFirstValue(Constants.Auth.AbsoluteExpiryClaim);
		if (stamp is null || expiry is null)
			return false;

		if (!long.TryParse(expiry, NumberStyles.None, CultureInfo.InvariantCulture, out var expirySeconds)
			|| _time.GetUtcNow().ToUnixTimeSeconds() >= expirySeconds)
			return false;

		return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stamp), Encoding.UTF8.GetBytes(CurrentStamp()));
	}

	/// <summary>Invalidates every outstanding login.</summary>
	public void RevokeAll()
	{
		_epoch.Bump();
	}

	/// <summary>Hash of the configured password hash, stable across logouts (used for the trusted-device cookie).</summary>
	public string PasswordStamp()
	{
		return Hex(_options.Value.Auth.PasswordHash);
	}

	private string CurrentStamp()
	{
		return Hex(_options.Value.Auth.PasswordHash + "\n" + _epoch.Current);
	}

	private static string Hex(string value)
	{
		return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
	}
}
