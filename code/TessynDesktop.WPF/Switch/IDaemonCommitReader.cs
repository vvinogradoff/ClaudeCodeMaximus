namespace TessynDesktop.WPF.Switch;

/// <summary>Reads the git commit a daemon build was made from (FR.32.9).</summary>
/// <remarks>Created by Claude</remarks>
public interface IDaemonCommitReader
{
	/// <summary>The commit in the informational version of <c>TessynDesktop.dll</c> in <paramref name="folder"/>; null when it cannot be read.</summary>
	string? ReadCommit(string folder);
}
