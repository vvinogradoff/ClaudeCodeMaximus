namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>session.changed</c>: the session's fresh state.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionChangedPayload(SessionStateDto State);
