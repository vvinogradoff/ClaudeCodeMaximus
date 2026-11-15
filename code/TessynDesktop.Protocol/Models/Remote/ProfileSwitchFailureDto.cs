using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>A session <c>profile.switch</c> could not move completely: it is not pinned to the new profile and its switch stays queued for the next turn.</summary>
/// <param name="NodeId">The session.</param>
/// <param name="Name">Its name.</param>
/// <param name="Paths">The files or folders that could not be moved (for example held open by a running CLI).</param>
/// <remarks>Created by Claude</remarks>
public sealed record ProfileSwitchFailureDto(string NodeId, string Name, IReadOnlyList<string> Paths);
