using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TessynDesktop.Codex;

/// <summary>
/// The GPT models Codex reports (FR.33.2), from the catalog profile's <c>model/list</c>, cached on disk for 24 hours. Never from a signed-out
/// profile, and never a curated list.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ICodexModelCatalog
{
	/// <summary>The GPT models, the <c>isDefault</c> one first, then in Codex's order; empty when there is no cache and no signed-in profile.</summary>
	IReadOnlyList<CodexCatalogModel> Models { get; }

	/// <summary>True once a list was loaded (from the cache or Codex), even an empty one.</summary>
	bool IsLoaded { get; }

	DateTimeOffset? FetchedAtUtc { get; }

	/// <summary>The profile the current list came from.</summary>
	string? SourceProfileId { get; }

	/// <summary>The model with this id (any case), else null.</summary>
	CodexCatalogModel? Find(string? modelId);

	/// <summary>Loads the cache; refreshes it when it is missing or older than 24 hours (daemon start). Never throws.</summary>
	Task StartAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Fetches the list again from the catalog profile (<c>models.refresh</c>, a sign-in). A failed fetch keeps the current list and logs a
	/// Warning. True when a new list was stored.
	/// </summary>
	Task<bool> RefreshAsync(CancellationToken cancellationToken = default);

	/// <summary>Raised after the list changed.</summary>
	event EventHandler? Updated;
}
