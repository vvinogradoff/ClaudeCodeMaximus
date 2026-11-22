namespace TessynDesktop.Codex;

/// <summary>The <c>error</c> member of a JSON-RPC response.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexRpcError(int Code, string Message);
