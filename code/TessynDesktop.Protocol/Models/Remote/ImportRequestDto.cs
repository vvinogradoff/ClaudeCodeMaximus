namespace TessynDesktop.Models.Remote;

/// <summary>One session to import with <c>import.run</c>.</summary>
/// <param name="ExternalId">The Claude session id.</param>
/// <param name="Name">The name of the new session; omitted = the title, else the first prompt, else "Imported Session".</param>
/// <remarks>Created by Claude</remarks>
public sealed record ImportRequestDto(string ExternalId, string? Name = null);
