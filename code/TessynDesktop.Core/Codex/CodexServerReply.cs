namespace TessynDesktop.Codex;

/// <summary>
/// Tessyn's answer to a <see cref="CodexServerRequest"/>: either a <see cref="Result"/> (serialized with the Codex options) or an <see cref="Error"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexServerReply(object? Result, CodexRpcError? Error)
{
	public static CodexServerReply Success(object result) => new(result, null);

	public static CodexServerReply Failure(int code, string message) => new(null, new CodexRpcError(code, message));
}
