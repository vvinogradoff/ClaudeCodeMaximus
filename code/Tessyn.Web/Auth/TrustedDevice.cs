using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Tessyn.Web.Auth;

/// <summary>"Trusted device" cookie, issued on every successful login; lets the owner's browser skip the global failure limit.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TrustedDevice
{
	private readonly IDataProtector _protector;
	private readonly AuthSessionService _session;
	private readonly TimeProvider _time;

	public TrustedDevice(IDataProtectionProvider provider, AuthSessionService session, TimeProvider time)
	{
		_protector = provider.CreateProtector(Constants.Auth.DeviceProtectorPurpose);
		_session = session;
		_time = time;
	}

	public bool IsTrusted(HttpRequest request)
	{
		if (!request.Cookies.TryGetValue(Constants.Auth.DeviceCookieName, out var cookie) || string.IsNullOrEmpty(cookie))
			return false;

		try
		{
			var parts = _protector.Unprotect(cookie).Split(Constants.Auth.DevicePayloadSeparator);
			if (parts.Length != 3 || parts[0] != Constants.Auth.DevicePayloadVersion)
				return false;

			if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(parts[1]), Encoding.UTF8.GetBytes(_session.PasswordStamp())))
				return false;

			return long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expiry)
				&& _time.GetUtcNow().ToUnixTimeSeconds() < expiry;
		}
		catch (CryptographicException)
		{
			return false;
		}
	}

	public void Issue(HttpResponse response)
	{
		var expires = _time.GetUtcNow().AddDays(Constants.Auth.DeviceCookieDays);
		var payload = string.Join(
			Constants.Auth.DevicePayloadSeparator,
			Constants.Auth.DevicePayloadVersion,
			_session.PasswordStamp(),
			expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
		response.Cookies.Append(Constants.Auth.DeviceCookieName, _protector.Protect(payload), new CookieOptions
		{
			HttpOnly = true,
			Secure = true,
			SameSite = SameSiteMode.Strict,
			Path = "/",
			Expires = expires,
			IsEssential = true,
		});
	}
}
