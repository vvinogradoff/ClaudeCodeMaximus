using TessynDesktop.Models.Remote;

namespace TessynDesktop.Models.Turns;

/// <summary>A tool call of the turn started or returned.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record TurnToolProgress(string NodeId, string TurnId, string ToolUseId, string Name, TurnToolStatus Status, string? Summary, bool? IsError)
	: TurnProgress(NodeId, TurnId);
