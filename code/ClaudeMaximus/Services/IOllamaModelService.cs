using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <summary>
/// Discovers models installed in a locally running Ollama instance (FR.12.13).
/// Returns an empty list silently if Ollama is unreachable.
/// </summary>
public interface IOllamaModelService
{
    Task<IReadOnlyList<ClaudeModelInfo>> GetModelsAsync(string baseUrl, CancellationToken cancellationToken = default);
}
