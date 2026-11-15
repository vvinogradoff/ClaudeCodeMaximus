using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Characterisation of the git-origin move rule exactly as <c>SessionTreeViewModel.CanMoveSessionTo</c> had it before it moved to Core
/// (P1.J1): the expectations come from that code, not from the extraction. The Avalonia project is not referenced by this test project,
/// so the rule is pinned here and the view model now calls it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeMoveRulesTests
{
	private readonly TreeMoveRules _rules = new();

	[Theory]
	[InlineData(null, null, true)]                                            // no origin anywhere: unrestricted
	[InlineData(null, "git@github.com:a/b.git", true)]                         // a session without an origin may enter a git directory
	[InlineData("git@github.com:a/b.git", null, false)]                        // a target without an origin cannot accept an origined session
	[InlineData("git@github.com:a/b.git", "git@github.com:a/b.git", true)]     // same repository (another worktree)
	[InlineData("git@github.com:a/b.git", "GIT@GITHUB.COM:A/B.GIT", true)]     // compared ignoring case
	[InlineData("git@github.com:a/b.git", "git@github.com:a/c.git", false)]    // another repository
	[InlineData("git@github.com:a/b.git", "git@github.com:a/b", false)]        // no URL normalisation: .git matters
	[InlineData("git@github.com:a/b.git", "", false)]                          // an empty origin is not null, and not equal
	[InlineData("", "", true)]                                                 // two empty origins are equal, as string.Equals says
	public void CanMove_FollowsTheRuleTheAvaloniaViewModelHad(string? source, string? target, bool expected) =>
		Assert.Equal(expected, _rules.CanMove(source, target));
}
