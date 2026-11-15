namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>session.resetConversation</c>.</summary>
/// <param name="Cleared">The Claude session id was detached; false when the session had none.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionResetConversationResult(bool Cleared);
