namespace TessynDesktop.Models.Remote;

/// <summary>One effort a model offers (FR.33.3, tdcp-spec 10.A14): the value to send and, for GPT models, Codex's description (the picker's tooltip).</summary>
/// <param name="Id">The effort value (<c>low</c>, <c>medium</c>, <c>high</c>, <c>max</c>, ...); never <c>default</c>.</param>
/// <param name="Description">Codex's description; omitted for Claude and Ollama models.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ModelEffortDto(string Id, string? Description = null);
