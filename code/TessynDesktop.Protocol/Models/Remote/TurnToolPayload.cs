namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>turn.tool</c>: a tool call started or returned. Never dropped.</summary>
/// <param name="NodeId">The session the turn belongs to.</param>
/// <param name="TurnId">The turn.</param>
/// <param name="ToolUseId">Pairs the <c>started</c> and the <c>result</c> notification.</param>
/// <param name="Name">The tool's name (e.g. <c>Bash</c>, <c>Read</c>).</param>
/// <param name="Status">Started or result.</param>
/// <param name="Summary">One line about the call (the command, file or pattern) or about the result; truncated.</param>
/// <param name="IsError">For a result: the tool reported an error.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TurnToolPayload(string NodeId, string TurnId, string ToolUseId, string Name, TurnToolStatus Status, string? Summary = null, bool? IsError = null);
