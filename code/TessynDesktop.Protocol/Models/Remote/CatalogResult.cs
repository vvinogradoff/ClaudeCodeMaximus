using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Answer of catalog.get: what a turn can be sent with.</summary>
/// <remarks>MediaPreviewHosts (additive): hosts whose images and videos a client may load without asking (decision A13); an entry like <c>*.example.com</c> covers subdomains. Created by Claude</remarks>
public sealed record CatalogResult(
	IReadOnlyList<ModelOptionDto> Models,
	IReadOnlyList<string> Efforts,
	IReadOnlyList<ProfileOptionDto> Profiles,
	IReadOnlyList<string>? MediaPreviewHosts = null);
