namespace TessynDesktop.Models.Remote;

/// <summary>
/// A ChatGPT profile as <c>profile.list</c> reports it in <c>codexProfiles</c> (FR.33.5, FR.33.17, tdcp-spec 10.A14). Never carries its <c>CODEX_HOME</c> folder.
/// </summary>
/// <param name="Id"><c>codex-default</c> (the User's own <c>%USERPROFILE%\.codex</c>) or a stored <c>codex_N</c>.</param>
/// <param name="DisplayName"><c>ChatGPT · &lt;email&gt;</c>.</param>
/// <param name="Email">The account e-mail once known.</param>
/// <param name="Plan">The ChatGPT plan type (<c>plus</c>, <c>pro</c>, ...) once known.</param>
/// <param name="IsCodexDefault">True for Codex Default, which can't be removed.</param>
/// <param name="SignedIn">The profile was signed in when last checked (or has a recorded account).</param>
/// <param name="AuthError">The profile needs <b>Sign in again</b> (FR.33.13).</param>
/// <param name="IsCatalogProfile">It is the configured catalog profile (<c>codexCatalogProfileId</c>, FR.33.2).</param>
/// <remarks>Created by Claude</remarks>
public sealed record CodexProfileDto(string Id, string DisplayName, string? Email, string? Plan, bool IsCodexDefault, bool SignedIn, bool AuthError, bool IsCatalogProfile);
