using System.Collections.Generic;

namespace ClaudeMaximus.Models;

/// <summary>
/// Response from the commands.list RPC.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynCommandsListResult
{
    public List<TessynCommand> Commands { get; init; } = [];
}
