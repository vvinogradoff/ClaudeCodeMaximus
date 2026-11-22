using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Codex;

/// <summary>
/// One GPT model of the catalog (FR.33.2) as Tessyn keeps it from <c>model/list</c>: the id it stores and sends, the name, whether Codex marks
/// it default, its efforts in Codex's order without <c>ultra</c> (FR.33.3), its default effort, its input modalities and Codex's upgrade hint.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexCatalogModel(
	string Id,
	string DisplayName,
	bool IsDefault,
	IReadOnlyList<ModelEffortOptionModel> Efforts,
	string? DefaultEffort,
	IReadOnlyList<string> InputModalities,
	string? Upgrade);
