using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// The hook for deriving a session's engine from its model (FR.33.4). The router asks it before it falls back to the stored
/// <see cref="SessionNodeModel.EngineId"/>. Phase 3 implements it with the GPT catalog (FR.33.2); until then nothing is registered and the
/// stored id decides.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionEngineSelector
{
	/// <summary>The id of the engine the session's next turn runs on, or null to keep <see cref="SessionNodeModel.EngineId"/>.</summary>
	string? SelectEngineId(SessionNodeModel node);
}
