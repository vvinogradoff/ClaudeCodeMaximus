namespace TessynDesktop.Models.Remote;

/// <summary>Answer of session.setFavorite.</summary>
/// <param name="Value">The favourite flag after the call.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SetFavoriteResult(bool Value);
