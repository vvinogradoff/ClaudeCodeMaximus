namespace TessynDesktop.Codex;

/// <summary>A <c>thread/resume</c> or <c>turn/start</c> failed in one of the known ways of FR.33.7 (<see cref="Kind"/>).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexThreadException : CodexRpcException
{
	public CodexThreadErrorKind Kind { get; }
	public string ThreadId { get; }

	public CodexThreadException(CodexThreadErrorKind kind, string threadId, CodexRpcException inner)
		: base(inner.Method, inner.Code, inner.Message, inner.ErrorData, inner)
	{
		Kind     = kind;
		ThreadId = threadId;
	}
}
