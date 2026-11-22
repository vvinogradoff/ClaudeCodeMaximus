namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>profiles.codexLogin</c>: the sign-in has started; <c>profiles.codexLoginCompleted</c> follows (FR.33.5).</summary>
/// <param name="ProfileId">The profile being signed in: a new <c>codex_N</c> (added only on success) or the one asked for.</param>
/// <param name="LoginId">Codex's login id; pass it to <c>profiles.codexLoginCancel</c> and match it in the completion event.</param>
/// <param name="Mode"><c>browser</c> or <c>deviceCode</c>.</param>
/// <param name="AuthUrl">Browser mode: the URL the client opens in the system browser (Codex hosts the localhost callback).</param>
/// <param name="VerificationUrl">Device-code mode: where the User enters the code.</param>
/// <param name="UserCode">Device-code mode: the code to show.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ProfileCodexLoginResult(string ProfileId, string LoginId, string Mode, string? AuthUrl, string? VerificationUrl, string? UserCode);
