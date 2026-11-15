namespace TessynDesktop.Services;

/// <summary>The outcome of <see cref="IProfileAdminService.AuthenticateAsync"/>.</summary>
/// <param name="Ran">False when a login for the profile was already running and nothing was started.</param>
/// <param name="Email">The account that is logged in afterwards; null when the login was cancelled or failed.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ProfileAuthentication(bool Ran, string? Email);
