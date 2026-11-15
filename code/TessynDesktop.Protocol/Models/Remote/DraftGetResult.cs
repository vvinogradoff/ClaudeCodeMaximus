namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>draft.get</c>: the unsent text of a session (empty when there is none) and its revision.</summary>
/// <param name="Text">The draft text; empty when the session has no draft.</param>
/// <param name="Revision">Identifies this text; <see cref="RemoteProtocol.Composer.EmptyDraftRevision"/> when there is no draft. Pass it as <c>baseRevision</c> of <c>draft.set</c>.</param>
/// <remarks>Created by Claude</remarks>
public sealed record DraftGetResult(string Text, string Revision);
