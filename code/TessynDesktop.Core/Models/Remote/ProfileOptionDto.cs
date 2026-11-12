namespace TessynDesktop.Models.Remote;

/// <summary>A Claude profile the client may pick (contract section 3.20); never carries a config directory.</summary>
/// <param name="Id">"default" or a stored profile's id.</param>
/// <param name="DisplayName">Name to show.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ProfileOptionDto(string Id, string DisplayName);
