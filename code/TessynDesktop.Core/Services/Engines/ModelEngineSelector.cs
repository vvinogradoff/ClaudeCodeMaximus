using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionEngineSelector" />
/// <remarks>
/// One exception to "not in the catalog is Claude": a GPT-looking id while the GPT list was never loaded (no cache yet, nobody signed in) gives no
/// answer, so a Codex session is not moved to Claude, and its thread detached, only because the catalog is not there. Created by Claude.
/// </remarks>
public sealed class ModelEngineSelector : ISessionEngineSelector
{
	private readonly ISessionModelCatalog _catalog;

	public ModelEngineSelector(ISessionModelCatalog catalog) => _catalog = catalog;

	/// <inheritdoc />
	public string? SelectEngineId(SessionNodeModel node, string? modelId)
	{
		if (_catalog.IsGpt(modelId))
			return Constants.SessionEngines.Codex;

		if (_catalog.LooksLikeGpt(modelId) && !_catalog.IsGptCatalogLoaded)
			return null;

		return Constants.SessionEngines.Claude;
	}
}
