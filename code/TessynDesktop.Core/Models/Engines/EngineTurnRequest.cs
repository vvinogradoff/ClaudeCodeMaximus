using TessynDesktop.Models.Turns;
using TessynDesktop.Services;

namespace TessynDesktop.Models.Engines;

/// <summary>One turn for <see cref="ISessionEngine.RunTurnAsync"/>. The USER entry is already in the transcript and the caller holds the turn lock.</summary>
/// <param name="Node">The session.</param>
/// <param name="Prompt">What the engine sends: the user's text with the instruction block, or the context preamble around it (FR.11.10).</param>
/// <param name="AugmentedPrompt">The user's text with the instruction block, without a preamble. Used when the engine has to seed a new conversation from the transcript.</param>
/// <param name="ConversationId">The engine's conversation id the turn resumes, as the caller read it from <see cref="ISessionEngine.GetConversationId"/>; null starts a new conversation.</param>
/// <param name="Settings">The model, effort, profile and launch flags of the turn.</param>
/// <param name="Preparation">What <see cref="ISessionEngine.PrepareTurnAsync"/> returned for this turn.</param>
/// <param name="Mode">Which pipeline runs the turn.</param>
/// <param name="Persistent">Run the turn in the session's persistent process (Streaming Session Mode, FR.19), starting it when needed.</param>
/// <param name="PersistentSink">
/// With <paramref name="Persistent"/>: where the persistent process reports everything for as long as it lives. The engine also reports the
/// current turn there; the caller routes it to the running turn or, outside any turn, to the background (FR.19.4).
/// </param>
/// <remarks>Created by Claude</remarks>
public sealed record EngineTurnRequest(
	SessionNodeModel Node,
	string Prompt,
	string AugmentedPrompt,
	string? ConversationId,
	TurnSettings Settings,
	EngineTurnPreparation? Preparation,
	EngineTurnMode Mode,
	bool Persistent = false,
	IEngineEventSink? PersistentSink = null);
