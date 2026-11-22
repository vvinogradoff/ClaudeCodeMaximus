namespace TessynDesktop.Models.Remote;

/// <summary>
/// The action an Attention Item carries (tdcp-spec 10.A13): <c>{ type: "elevatedRun", run }</c> on the item of FR.23.31. A client that does not know
/// <c>action</c> shows a plain Ask.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record AttentionActionDto(string Type, ElevatedRunDto Run);
