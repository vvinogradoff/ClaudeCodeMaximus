namespace Tessyn.Web.Options;

/// <remarks>Created by Claude</remarks>
public sealed class TessynWebOptions
{
	public AuthOptions Auth { get; set; } = new();
	public LinkOptions Link { get; set; } = new();
	public string DataProtectionKeysPath { get; set; } = string.Empty;
}
