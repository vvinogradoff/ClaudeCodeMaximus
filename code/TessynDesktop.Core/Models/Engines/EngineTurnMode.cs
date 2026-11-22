namespace TessynDesktop.Models.Engines;

/// <summary>Which pipeline runs a turn. The engine's recovery rules differ between them (FR.11.10, FR.19.13).</summary>
/// <remarks>Created by Claude</remarks>
public enum EngineTurnMode
{
	/// <summary>A turn sent from a client (<c>TurnEngine</c>): a lost conversation is recovered within the turn from the transcript.</summary>
	Interactive,

	/// <summary>A headless turn (<c>SessionTurnService</c>: orchestration, schedules, Telegram): resumability is checked before the turn, so a lost conversation is an error.</summary>
	Headless,
}
