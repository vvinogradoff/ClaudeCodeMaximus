namespace TessynDesktop.Models.Remote;

/// <summary>
/// The read-only Codex status of <c>client.hello</c>, for every client including the relay (FR.33.17, tdcp-spec 10.A14, decision 2026-11-22).
/// It carries no path and no profile data; the full <see cref="CodexStatusDto"/> with the path stays in the loopback-only <c>settings.general</c>.
/// </summary>
/// <param name="Found">A usable native <c>codex.exe</c> was found with the configured path (empty = find it).</param>
/// <param name="Version">The version <c>codex --version</c> reported (<c>0.171.0</c>); omitted when it could not be read.</param>
/// <param name="TestedVersion">The version this build was tested with.</param>
/// <param name="Note">The version note (<c>Codex 0.172.0 is newer than the tested 0.171.x ...</c>); omitted for the tested version.</param>
/// <remarks>Created by Claude</remarks>
public sealed record CodexHelloStatusDto(bool Found, string? Version, string TestedVersion, string? Note);
