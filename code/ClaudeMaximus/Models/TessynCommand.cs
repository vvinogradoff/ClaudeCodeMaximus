using System.Collections.Generic;

namespace ClaudeMaximus.Models;

/// <summary>
/// A command or skill definition returned by the daemon's commands.list RPC.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynCommand
{
    /// <summary>Command name without the leading slash (e.g. "compact", "model").</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable description for autocomplete display.</summary>
    public string? Description { get; init; }

    /// <summary>"builtin" or "skill".</summary>
    public string Type { get; init; } = "builtin";

    /// <summary>Source file path for skills. Null for builtins.</summary>
    public string? Source { get; init; }

    /// <summary>Argument definitions for autocomplete and validation.</summary>
    public List<TessynCommandArg> Args { get; init; } = [];
}

/// <summary>
/// An argument definition for a command.
/// </summary>
public sealed class TessynCommandArg
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool Required { get; init; }
    public List<string>? Choices { get; init; }
}
