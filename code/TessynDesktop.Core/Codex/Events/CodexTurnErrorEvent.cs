namespace TessynDesktop.Codex.Events;

/// <summary>
/// A turn error that ends the turn (an <c>error</c> with <c>willRetry: false</c>, or a failed <c>turn/completed</c>): one SYSTEM entry
/// <see cref="SystemText"/>. <see cref="Code"/> is the <c>codexErrorInfo</c> name, when there is one.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexTurnErrorEvent(string Message, CodexErrorKind Kind, string? Code, string SystemText) : CodexTurnEvent;
