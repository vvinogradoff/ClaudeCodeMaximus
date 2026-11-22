namespace TessynDesktop.Models;

/// <summary>One effort a model offers (FR.33.3): its value and, for GPT models, Codex's description (the picker's tooltip).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ModelEffortOptionModel(string Effort, string? Description);
