namespace TessynDesktop.Models.Remote;

/// <summary>Payload of the usage.changed notification.</summary>
/// <param name="ProfileId">"default" or a stored profile's id.</param>
/// <remarks>Created by Claude</remarks>
public sealed record UsageChangedPayload(string ProfileId);
