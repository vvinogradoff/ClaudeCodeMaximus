namespace TessynDesktop.Models;

/// <summary>
/// The metadata <c>LastUsedSettingsResolver</c> works from, found through the entry index: the profile of the last USER entry that
/// recorded one, and the model and effort of the last ASSISTANT entry that recorded a model.
/// </summary>
/// <param name="ProfileName">Profile name recorded on the last USER entry that has one.</param>
/// <param name="ModelId">Model recorded on the last ASSISTANT entry that has one.</param>
/// <param name="Effort">Effort recorded on that same ASSISTANT entry.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionContentLastUsedModel(string? ProfileName, string? ModelId, string? Effort);
