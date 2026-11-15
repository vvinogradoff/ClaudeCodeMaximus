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

		/// <summary>Longest <c>clientId</c> of <c>client.register</c> / <c>client.hello</c>.</summary>
		public const int MaxClientIdLength = 64;

		/// <summary>
		/// How often a local SSE stream writes a comment line when it has nothing else to say. A stream whose client is gone fails on such a write, so its
		/// notification registration ends within about this time instead of keeping the host silent.
		/// </summary>
		public static readonly TimeSpan SseKeepAliveInterval = TimeSpan.FromSeconds(15);

		/// <summary>Kind of a file suggestion (path and file modes).</summary>
		public const string FileKind = "file";

		/// <summary>Kind of a directory suggestion (path mode).</summary>
		public const string DirectoryKind = "directory";
	}
}
