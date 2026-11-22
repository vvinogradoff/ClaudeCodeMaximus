using System;
using System.Collections.Generic;

namespace TessynDesktop.Codex;

/// <summary>The GPT catalog cache file <c>codex-models.json</c> (FR.33.2): the models, when they were fetched and from which profile.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexModelCacheModel
{
	public DateTimeOffset FetchedAtUtc { get; set; }

	public string? SourceProfileId { get; set; }

	public List<CodexCatalogModel> Models { get; set; } = [];
}
