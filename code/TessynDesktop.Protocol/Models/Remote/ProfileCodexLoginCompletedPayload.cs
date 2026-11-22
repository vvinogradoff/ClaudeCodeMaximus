namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>profiles.codexLoginCompleted</c> (local stream only): how a ChatGPT sign-in ended (FR.33.5).</summary>
/// <param name="LoginId">The id <c>profiles.codexLogin</c> answered.</param>
/// <param name="ProfileId">The profile signed in (on success it is now listed).</param>
/// <param name="Success">True when the account was read after the sign-in.</param>
/// <param name="Error">Codex's error text for a failure or a cancel; nothing was added then.</param>
/// <param name="Email">The account e-mail on success.</param>
/// <param name="Plan">The ChatGPT plan type on success.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ProfileCodexLoginCompletedPayload(string LoginId, string ProfileId, bool Success, string? Error, string? Email, string? Plan);
