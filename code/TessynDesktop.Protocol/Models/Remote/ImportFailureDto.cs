namespace TessynDesktop.Models.Remote;

/// <summary>A session <c>import.run</c> could not import; the reason never carries a path.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ImportFailureDto(string ExternalId, string Reason);
