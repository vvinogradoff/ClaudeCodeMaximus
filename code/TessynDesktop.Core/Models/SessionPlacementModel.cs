namespace TessynDesktop.Models;

/// <summary>Where <see cref="Services.ISessionRegistry.CreateSessionAfter"/> put a new session.</summary>
/// <param name="Session">The new session.</param>
/// <param name="Directory">The Directory node that contains it (directly or through <paramref name="Group"/>).</param>
/// <param name="Group">The Group node that holds it directly; null when it sits directly under <paramref name="Directory"/>.</param>
/// <param name="Index">Index within the container's <c>Sessions</c> list.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionPlacementModel(SessionNodeModel Session, DirectoryNodeModel Directory, GroupNodeModel? Group, int Index);
