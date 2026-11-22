using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Derives a turn's engine from its resolved model (FR.33.4): a GPT model of the catalog runs on <c>codex</c>; <c>default</c>, Claude, Ollama
/// and models in no catalog run on <c>claude</c>. The router asks it when a turn starts and stores the answer in
/// <see cref="SessionNodeModel.EngineId"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionEngineSelector
{
	/// <summary>The id of the engine a turn on <paramref name="modelId"/> runs on, or null when it can't tell (the stored id then decides).</summary>
	string? SelectEngineId(SessionNodeModel node, string? modelId);
}
