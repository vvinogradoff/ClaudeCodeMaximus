using TessynDesktop.Models;

namespace TessynDesktop.Models.Remote;

/// <summary>A chip of the Tags tab (contract 3.8).</summary>
/// <param name="Key">Canonical key.</param>
/// <param name="Label">Display label.</param>
/// <param name="Url">Link, or empty when <see cref="Services.SessionTagUrlPolicy"/> does not allow it.</param>
/// <param name="Kind">Tag kind.</param>
/// <param name="SessionCount">Top-level sessions in scope carrying the tag; 0 for a stub of a vanished selected tag.</param>
/// <param name="Selected">The client had the tag selected.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TagChipDto(string Key, string Label, string Url, SessionTagKind Kind, int SessionCount, bool Selected);
