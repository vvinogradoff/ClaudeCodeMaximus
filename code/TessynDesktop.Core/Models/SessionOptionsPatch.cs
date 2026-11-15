namespace TessynDesktop.Models;

/// <summary>
/// A change to a session's instruction options (<c>session.setOptions</c>). A null member, or <see cref="ChangeAutoCommit"/> false, leaves
/// that option alone; Auto-Commit is tri-state, so "set it to No preference" needs the flag besides a null value.
/// </summary>
/// <param name="ChangeAutoCommit">Auto-Commit is part of the change.</param>
/// <param name="AutoCommit">The new Auto-Commit value (true = Commit, false = Do-not-commit, null = No preference); read only with <paramref name="ChangeAutoCommit"/>.</param>
/// <param name="AutoDocument">The new Auto-Document value, or null to leave it.</param>
/// <param name="PlayCompletionSound">The new completion-sound value, or null to leave it.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionOptionsPatch(bool ChangeAutoCommit, bool? AutoCommit, bool? AutoDocument, bool? PlayCompletionSound);
