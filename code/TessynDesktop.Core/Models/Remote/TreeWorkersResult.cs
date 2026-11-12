using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>tree.workers</c> (contract 3.3).</summary>
/// <param name="SupervisorId">The supervisor's NodeId.</param>
/// <param name="Workers">Its direct Worker Sessions in model order, each carrying <c>supervisorId</c>.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TreeWorkersResult(string SupervisorId, IReadOnlyList<TreeSessionDto> Workers);
