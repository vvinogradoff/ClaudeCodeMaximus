namespace TessynDesktop.Models.Remote;

/// <summary>
/// Read-only Codex status of the <c>general</c> settings section (FR.33.17, FR.33.6, tdcp-spec 10.A14). Filled by <c>settings.get</c> at the time
/// of the call (it runs <c>codex --version</c>); it never bumps the section version.
/// </summary>
/// <param name="Found">A usable native <c>codex.exe</c> was found with the configured path (empty = find it).</param>
/// <param name="Path">The executable Tessyn launches; omitted when not found.</param>
/// <param name="Version">The version <c>codex --version</c> reported (<c>0.171.0</c>); omitted when it could not be read.</param>
/// <param name="TestedVersion">The version this build was tested with.</param>
/// <param name="Note">The version note (<c>Codex 0.172.0 is newer than the tested 0.171.x ...</c>); omitted for the tested version.</param>
/// <param name="StatusLine">The whole status line Settings shows (<c>Codex 0.171.0 found at ...</c> or the reason nothing was found).</param>
/// <remarks>Created by Claude</remarks>
public sealed record CodexStatusDto(bool Found, string? Path, string? Version, string TestedVersion, string? Note, string StatusLine);
