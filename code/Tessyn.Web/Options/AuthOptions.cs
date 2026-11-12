namespace Tessyn.Web.Options;

/// <remarks>Created by Claude</remarks>
public sealed class AuthOptions
{
	public string Email { get; set; } = string.Empty;
	public string PasswordHash { get; set; } = string.Empty;
	public int CookieDays { get; set; } = Constants.Defaults.CookieDays;
	public ThrottleOptions Throttle { get; set; } = new();
}
