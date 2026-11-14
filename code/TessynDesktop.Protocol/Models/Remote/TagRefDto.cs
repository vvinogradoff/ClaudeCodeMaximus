using TessynDesktop.Models;

namespace TessynDesktop.Models.Remote;

/// <summary>A tag selected by the client, sent back so a tag that has since left the index still shows as a stub chip (contract 3.8).</summary>
/// <param name="Key">Canonical key.</param>
/// <param name="Label">Display label.</param>
/// <param name="Url">Link; only emitted when the session-tag URL policy allows it.</param>
/// <param name="Kind">Tag kind.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TagRefDto(string Key, string Label, string Url, SessionTagKind Kind);
