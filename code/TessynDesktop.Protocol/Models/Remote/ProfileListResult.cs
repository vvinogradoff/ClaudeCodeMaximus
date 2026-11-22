using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>profile.list</c>.</summary>
/// <param name="Profiles">The Claude profiles: Default first, then the stored ones.</param>
/// <param name="CodexProfiles">The ChatGPT profiles (FR.33.5, additive): Codex Default first, then the stored <c>codex_N</c>; omitted when the daemon has no Codex engine.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ProfileListResult(IReadOnlyList<ProfileDto> Profiles, IReadOnlyList<CodexProfileDto>? CodexProfiles = null);
