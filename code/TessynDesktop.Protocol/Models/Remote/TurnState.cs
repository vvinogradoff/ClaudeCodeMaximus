namespace TessynDesktop.Models.Remote;

/// <summary>The states a <c>turn.status</c> reports.</summary>
/// <remarks>Created by Claude</remarks>
public enum TurnState
{
	/// <summary>Accepted, waiting behind another turn of the session.</summary>
	Queued,

	/// <summary>Holding the session's turn lock; claude is working.</summary>
	Running,

	/// <summary>Being run again after a transient failure; <c>detail</c> names the reason.</summary>
	Retrying,
}
