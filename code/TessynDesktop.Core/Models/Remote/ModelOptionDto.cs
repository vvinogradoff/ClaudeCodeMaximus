namespace TessynDesktop.Models.Remote;

/// <summary>A model the client may pick (contract section 3.20).</summary>
/// <param name="Id">"default" or a model id.</param>
/// <param name="DisplayName">Name to show.</param>
/// <param name="Provider">Where the model runs; omitted for the "default" entry.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ModelOptionDto(string Id, string DisplayName, ModelProvider? Provider);
