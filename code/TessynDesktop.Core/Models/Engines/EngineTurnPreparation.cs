namespace TessynDesktop.Models.Engines;

/// <summary>
/// What an engine prepared for a turn before the turn waits for the session's turn lock (for Claude: the MCP config file and the Local Tool
/// Profile). Opaque to the caller: it is handed back to the same engine in <see cref="EngineTurnRequest.Preparation"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public abstract record EngineTurnPreparation;
