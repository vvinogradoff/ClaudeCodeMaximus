namespace TessynDesktop.Models;

/// <summary>What <c>ProfilePinMigration</c> did to the session pins when the settings loaded (ISSUE.013).</summary>
/// <param name="Migrated">Display-name pins converted to <see cref="SessionNodeModel.ProfileId"/> pins.</param>
/// <param name="Unmatched">Legacy name-only records that match no stored profile (they run on Default with a warning).</param>
/// <param name="SystemLabels"><c>[Sys]</c> labels: the Default profile.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ProfilePinMigrationSummary(int Migrated, int Unmatched, int SystemLabels);
