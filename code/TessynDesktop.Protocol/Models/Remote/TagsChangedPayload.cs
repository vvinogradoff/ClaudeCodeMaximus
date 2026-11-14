namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>tags.changed</c>; a null <paramref name="NodeId"/> means every session's tags may have changed.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record TagsChangedPayload(string? NodeId);
