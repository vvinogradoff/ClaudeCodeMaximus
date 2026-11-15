namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>draft.changed</c>: the session whose draft changed and the draft's new revision.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record DraftChangedPayload(string NodeId, string Revision);
