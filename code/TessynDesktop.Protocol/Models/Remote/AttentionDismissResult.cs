namespace TessynDesktop.Models.Remote;

/// <summary>Answer of attention.dismiss.</summary>
/// <param name="Dismissed">An open item was closed; false for an unknown or already closed item.</param>
/// <remarks>Created by Claude</remarks>
public sealed record AttentionDismissResult(bool Dismissed);
