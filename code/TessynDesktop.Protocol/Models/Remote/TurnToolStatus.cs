namespace TessynDesktop.Models.Remote;

/// <summary>The two moments of a tool call a <c>turn.tool</c> reports.</summary>
/// <remarks>Created by Claude</remarks>
public enum TurnToolStatus
{
	/// <summary>The model asked for the tool.</summary>
	Started,

	/// <summary>The tool's result came back.</summary>
	Result,
}
