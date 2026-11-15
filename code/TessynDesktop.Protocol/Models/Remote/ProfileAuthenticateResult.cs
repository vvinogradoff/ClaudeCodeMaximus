namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>profile.authenticate</c>: false when a login for that profile is already running.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ProfileAuthenticateResult(bool Started);
