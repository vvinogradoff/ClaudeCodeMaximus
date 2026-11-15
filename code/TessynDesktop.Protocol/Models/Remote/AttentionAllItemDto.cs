namespace TessynDesktop.Models.Remote;

/// <summary>An attention item together with the session that owns it (<c>attention.listAll</c>).</summary>
/// <param name="NodeId">The owning session.</param>
/// <param name="SessionName">The session's name.</param>
/// <param name="Item">The item.</param>
/// <remarks>Created by Claude</remarks>
public sealed record AttentionAllItemDto(string NodeId, string SessionName, AttentionItemDto Item);
