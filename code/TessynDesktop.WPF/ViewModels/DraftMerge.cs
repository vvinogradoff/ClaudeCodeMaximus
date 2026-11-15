namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// Reconciles the text typed in this client with a draft another writer changed (a conflicting <c>draft.set</c>, a <c>draft.changed</c> from the desktop
/// composer or the phone). Neither side's text is ever dropped: when both changed, the daemon's text comes first and this client's text follows on a line of its own.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class DraftMerge
{
	/// <param name="local">What is in the input box now.</param>
	/// <param name="remote">What the daemon has now.</param>
	/// <param name="baseText">The text this client and the daemon last agreed on (null when unknown).</param>
	public static string Merge(string local, string remote, string? baseText)
	{
		if (local == remote || string.IsNullOrEmpty(remote) || remote == baseText)
			return local;

		if (string.IsNullOrEmpty(local) || local == baseText)
			return remote;

		if (local.Contains(remote, StringComparison.Ordinal))
			return local;

		if (remote.Contains(local, StringComparison.Ordinal))
			return remote;

		return remote.EndsWith('\n') ? remote + local : remote + Environment.NewLine + local;
	}
}
