namespace Tessyn.Web.Options;

/// <remarks>Created by Claude</remarks>
public sealed class LinkOptions
{
	public string TokenSha256 { get; set; } = string.Empty;
	public double RequestTimeoutSeconds { get; set; } = Constants.Defaults.RequestTimeoutSeconds;
	public double LongRequestTimeoutSeconds { get; set; } = Constants.Defaults.LongRequestTimeoutSeconds;
	public double PingSeconds { get; set; } = Constants.Defaults.PingSeconds;
	public int RingBufferEvents { get; set; } = Constants.Defaults.RingBufferEvents;
	public double RingBufferMinutes { get; set; } = Constants.Defaults.RingBufferMinutes;
}
