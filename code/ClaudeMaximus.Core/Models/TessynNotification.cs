using System.Text.Json;

namespace ClaudeMaximus.Models;

/// <summary>
/// A JSON-RPC 2.0 notification (no id field) received from the Tessyn daemon
/// via WebSocket push. Carries the method name and raw params for typed dispatch.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynNotification
{
    /// <summary>The notification method, e.g. "run.delta", "session.updated", "index.state_changed".</summary>
    public required string Method { get; init; }

    /// <summary>Raw JSON params element for downstream typed deserialization.</summary>
    public JsonElement Params { get; init; }
}
