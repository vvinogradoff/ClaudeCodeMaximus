namespace Tessyn.Web.Options;

/// <remarks>Created by Claude</remarks>
public sealed class AuthOptions
{
	public string Email { get; set; } = string.Empty;
	public string PasswordHash { get; set; } = string.Empty;
	/// <summary>Sliding idle timeout of the login cookie, in days.</summary>
	public int CookieDays { get; set; } = Constants.Defaults.CookieDays;

	/// <summary>Absolute lifetime of a login, in days, regardless of activity.</summary>
	public int AbsoluteDays { get; set; } = Constants.Defaults.AbsoluteDays;

	/// <summary>File holding the server-side auth epoch; empty = next to the Data Protection keys directory (in memory when that is unset too).</summary>
	public string EpochPath { get; set; } = string.Empty;
	public ThrottleOptions Throttle { get; set; } = new();
}
