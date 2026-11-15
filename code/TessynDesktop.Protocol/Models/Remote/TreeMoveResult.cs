namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>tree.moveNode</c>.</summary>
/// <param name="Moved">Always true; a refused move is a JSON-RPC error whose message gives the reason.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TreeMoveResult(bool Moved);
