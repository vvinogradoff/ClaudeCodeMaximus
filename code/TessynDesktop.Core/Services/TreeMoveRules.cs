using System;

namespace TessynDesktop.Services;

/// <summary>
/// The git-origin rule of moving a session between tree containers (P9.1). One source of truth for the desktop's drag-and-drop /
/// move mode and the daemon's <c>tree.moveNode</c>: a session whose directory has a git origin may only move into a container whose
/// directory has the same origin (the same repository, possibly another worktree); a session with no origin moves anywhere.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeMoveRules
{
	/// <summary>
	/// True when a session owned by a directory with <paramref name="sourceGitOrigin"/> may move into a container owned by a directory
	/// with <paramref name="targetGitOrigin"/>. Origins compare case-insensitively; a null source origin is unrestricted, a null target
	/// origin cannot accept a session that has one.
	/// </summary>
	public bool CanMove(string? sourceGitOrigin, string? targetGitOrigin)
	{
		if (sourceGitOrigin is null)
			return true;

		if (targetGitOrigin is null)
			return false;

		return string.Equals(sourceGitOrigin, targetGitOrigin, StringComparison.OrdinalIgnoreCase);
	}
}
