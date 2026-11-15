namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>draft.changed</c>: the session whose draft changed and the draft's new revision; <c>itemId</c> is present only for the reply draft of a Focus item (FR.23.19).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record DraftChangedPayload(string NodeId, string Revision, string? ItemId = null);
