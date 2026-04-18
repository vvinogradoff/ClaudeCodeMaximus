using System.Collections.Generic;

namespace ClaudeMaximus.Models;

/// <summary>Result of sessions.get including messages and durable metadata.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynSessionGetResult
{
    public required TessynSessionModel Session { get; init; }
    public List<TessynMessageModel> Messages { get; init; } = [];
    public TessynSessionMeta? Meta { get; init; }
}
