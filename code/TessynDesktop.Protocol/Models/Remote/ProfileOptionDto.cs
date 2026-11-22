namespace TessynDesktop.Models.Remote;

/// <summary>A profile the client may pick (contract section 3.20); never carries a config directory. <c>engine</c> and <c>plan</c> are additive (tdcp-spec 10.A14).</summary>
/// <param name="Id">"default" or a stored Claude profile's id; for a ChatGPT profile <c>codex-default</c> or <c>codex_N</c>.</param>
/// <param name="DisplayName">Name to show; a ChatGPT profile's is <c>ChatGPT · &lt;email&gt;</c>.</param>
/// <param name="Engine">The engine the profile belongs to; a profile is never used across engines (FR.33.5). Absent from an older daemon: <c>claude</c>.</param>
/// <param name="Plan">The ChatGPT plan type (for example <c>plus</c>); omitted for Claude profiles.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ProfileOptionDto(string Id, string DisplayName, string Engine = RemoteProtocol.Engines.Claude, string? Plan = null);
