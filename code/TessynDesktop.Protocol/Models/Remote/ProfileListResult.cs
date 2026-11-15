using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>profile.list</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ProfileListResult(IReadOnlyList<ProfileDto> Profiles);
