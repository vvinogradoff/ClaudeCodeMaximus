namespace TessynDesktop.Models;

/// <summary>
/// A ChatGPT sign-in that has started (FR.33.5): the client opens <see cref="AuthUrl"/> in the system browser, or shows <see cref="UserCode"/> and
/// <see cref="VerificationUrl"/> for a device-code sign-in. The daemon opens nothing itself.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexSignInStartModel(string LoginId, string ProfileId, bool IsDeviceCode, string? AuthUrl, string? VerificationUrl, string? UserCode);
