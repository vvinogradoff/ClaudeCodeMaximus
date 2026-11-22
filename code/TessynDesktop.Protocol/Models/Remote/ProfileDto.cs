namespace TessynDesktop.Models.Remote;

/// <summary>A profile as <c>profile.list</c> reports it; never carries a config directory.</summary>
/// <param name="Id">"default" or a stored profile's id.</param>
/// <param name="DisplayName">Name to show; the Default profile's is "[Sys] &lt;email&gt;".</param>
/// <param name="IsSystem">True for the Default (base, non-isolated) profile.</param>
/// <param name="Email">The Default profile's account e-mail, once resolved. Stored profiles carry theirs in <paramref name="DisplayName"/>.</param>
/// <param name="AuthState">See <see cref="ProfileAuthState"/>.</param>
/// <param name="IsDefault">True for the profile the app selects for a new session (the global selection).</param>
/// <param name="Engine">The engine of the profile (tdcp-spec 10.A14): <c>claude</c>, or <c>codex</c> for a ChatGPT profile that <c>profile.remove</c> answers.</param>
/// <param name="Plan">A ChatGPT profile's plan type; omitted for Claude profiles.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ProfileDto(string Id, string DisplayName, bool IsSystem, string? Email, ProfileAuthState AuthState, bool IsDefault, string Engine = RemoteProtocol.Engines.Claude, string? Plan = null);
