namespace TessynDesktop.Models.Remote;

/// <summary>A row of the Recent dropdown (contract 3.6).</summary>
/// <param name="Id">The session's NodeId.</param>
/// <param name="Name">Display name.</param>
/// <param name="DirectoryLabel">Label of the session's Directory node.</param>
/// <param name="State">Live state.</param>
/// <param name="HasSchedules">At least one schedule targets the session.</param>
/// <param name="Pinned">Favourite, open attention item or schedule: stays listed regardless of recency.</param>
/// <param name="CanHide">The row may be hidden (not pinned).</param>
/// <remarks>Created by Claude</remarks>
public sealed record RecentRowDto(string Id, string Name, string DirectoryLabel, SessionStateDto State, bool HasSchedules, bool Pinned, bool CanHide);
