using System;

namespace TessynDesktop;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Turn and composer extras of the remote API (package J2): drafts and autocomplete.</summary>
	public static class Composer
	{
		/// <summary>Window of <c>draft.changed</c>: a burst of draft saves announces the last revision once.</summary>
		public static readonly TimeSpan DraftChangedWindow = TimeSpan.FromMilliseconds(250);

		/// <summary>Message of the <c>draft.set</c> error for a stale <c>baseRevision</c>; the client re-reads the draft and merges.</summary>
		public const string DraftChangedMessage = "Draft changed";

		/// <summary>How long the daemon keeps a code index it had to build for <c>autocomplete.suggest</c> after the last request for its directory (ISSUE.016).</summary>
		public static readonly TimeSpan AutocompleteIndexIdle = TimeSpan.FromMinutes(5);

		/// <summary>How often idle autocomplete index leases are looked for.</summary>
		public static readonly TimeSpan AutocompleteLeaseSweepInterval = TimeSpan.FromMinutes(1);

		/// <summary>Kind of a file suggestion (path and file modes).</summary>
		public const string FileKind = "file";

		/// <summary>Kind of a directory suggestion (path mode).</summary>
		public const string DirectoryKind = "directory";
	}
}
