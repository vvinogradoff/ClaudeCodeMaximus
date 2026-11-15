using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Answer of catalog.get: what a turn can be sent with.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record CatalogResult(
	IReadOnlyList<ModelOptionDto> Models,
	IReadOnlyList<string> Efforts,
	IReadOnlyList<ProfileOptionDto> Profiles)
{
	/// <summary>
	/// Hosts whose https media previews load without a click (FR.26.4, decision A13): an exact host or <c>*.example.com</c>. Optional and
	/// additive: a daemon that predates it omits it, and a client then treats every host as click-to-load.
	/// </summary>
	public IReadOnlyList<string>? MediaPreviewHosts { get; init; }
}
