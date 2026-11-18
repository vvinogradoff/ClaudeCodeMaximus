namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>focus.itemCreated</c> (FR.22.6): a new Focus item appeared and the Focus item sound is due.</summary>
/// <param name="NodeId">The session that owns the item.</param>
/// <param name="ItemId">The item id, for example <c>A12</c>.</param>
/// <param name="Kind">The item kind name: <c>critical</c>, <c>ask</c>, <c>todo</c>, <c>alert</c> or <c>decided</c>.</param>
/// <param name="RaisedUtc">When the daemon raised it; a client drops one older than <see cref="RemoteProtocol.Notifications.MaxReplayAge"/> (a replay after a reconnect).</param>
/// <remarks>Created by Claude</remarks>
public sealed record FocusItemCreatedPayload(string NodeId, string ItemId, string Kind, DateTimeOffset RaisedUtc)
{
	/// <summary>True when this is a replay a client must drop.</summary>
	public bool IsReplay(DateTimeOffset now) => now - RaisedUtc > RemoteProtocol.Notifications.MaxReplayAge;
}
