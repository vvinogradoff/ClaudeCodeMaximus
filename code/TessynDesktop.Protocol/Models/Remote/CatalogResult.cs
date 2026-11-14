using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Answer of catalog.get: what a turn can be sent with.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record CatalogResult(
	IReadOnlyList<ModelOptionDto> Models,
	IReadOnlyList<string> Efforts,
	IReadOnlyList<ProfileOptionDto> Profiles);
