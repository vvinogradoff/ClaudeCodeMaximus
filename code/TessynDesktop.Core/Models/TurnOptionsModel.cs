namespace TessynDesktop.Models;

/// <summary>
/// Per-turn overrides for a headless turn (the web client's model, effort and profile choice). A null member means
/// "not specified": the session's usual resolution applies. Ignored when the turn is redirected into a live
/// Persistent Session Process (FR.19.13), because that process was already started with its own settings.
/// </summary>
/// <param name="ModelId">A model id; <c>"default"</c> = no --model flag.</param>
/// <param name="Effort">An effort value; null, blank or <c>"default"</c> = no --effort flag.</param>
/// <param name="ProfileName">A profile display name; the Default profile is expressed as its "[Sys] ..." label (matches no stored profile).</param>
/// <remarks>Created by Claude</remarks>
public sealed record TurnOptionsModel(string? ModelId = null, string? Effort = null, string? ProfileName = null);
