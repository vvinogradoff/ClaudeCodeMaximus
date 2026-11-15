namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>draft.set</c>: the revision of the draft as stored (<see cref="RemoteProtocol.Composer.EmptyDraftRevision"/> when the text was empty and the draft was deleted).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record DraftSetResult(string Revision);
