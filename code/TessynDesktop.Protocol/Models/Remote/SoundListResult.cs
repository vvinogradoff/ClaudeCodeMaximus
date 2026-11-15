using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>sound.list</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SoundListResult(IReadOnlyList<SoundDto> Sounds);
