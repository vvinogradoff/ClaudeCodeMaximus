namespace TessynDesktop.Models;

/// <summary>
/// The model and effort a turn runs with (FR.12.35): the <c>--model</c> and <c>--effort</c> values, <c>null</c> meaning no flag.
/// </summary>
/// <param name="ModelId">The <c>--model</c> value; null = no flag.</param>
/// <param name="Effort">The <c>--effort</c> value; null = no flag.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionModelSettingsModel(string? ModelId, string? Effort);
