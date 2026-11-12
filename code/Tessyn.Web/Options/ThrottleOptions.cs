namespace Tessyn.Web.Options;

/// <remarks>Created by Claude</remarks>
public sealed class ThrottleOptions
{
	public int PerIpFailures { get; set; } = Constants.Defaults.PerIpFailures;
	public double PerIpWindowMinutes { get; set; } = Constants.Defaults.PerIpWindowMinutes;
	public int GlobalFailures { get; set; } = Constants.Defaults.GlobalFailures;
	public double GlobalWindowMinutes { get; set; } = Constants.Defaults.GlobalWindowMinutes;
}
