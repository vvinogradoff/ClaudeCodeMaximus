namespace TessynDesktop.Models.Remote;

/// <summary>The per-session instruction options of <c>session.setOptions</c>.</summary>
/// <param name="AutoCommit">Tri-state (FR.11.3): true = Commit, false = Do-not-commit; omitted (null) = No preference.</param>
/// <param name="AutoDocument">Auto-Document (FR.11.5).</param>
/// <param name="PlayCompletionSound">Completion sound (FR.22.2).</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionOptionsDto(bool? AutoCommit, bool AutoDocument, bool PlayCompletionSound);
