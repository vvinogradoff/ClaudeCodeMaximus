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

		/// <summary>First line of an item draft file that also holds question values (FR.23.37); see <see cref="TessynDesktop.Services.ItemDraftCodec"/>.</summary>
		public const string ItemDraftEnvelopePrefix = "#tessyn-questionnaire-draft-v1";

		/// <summary>How long the daemon keeps a code index it had to build for <c>autocomplete.suggest</c> after the last request for its directory (ISSUE.016).</summary>
		public static readonly TimeSpan AutocompleteIndexIdle = TimeSpan.FromMinutes(5);

		/// <summary>How often idle autocomplete index leases are looked for.</summary>
		public static readonly TimeSpan AutocompleteLeaseSweepInterval = TimeSpan.FromMinutes(1);

		/// <summary>How long the host waits for a presenting client's <c>notification.ack</c> before it shows the toast or plays the sound itself (V3).</summary>
		public static readonly TimeSpan NotificationAckTimeout = TimeSpan.FromSeconds(3);

		/// <summary>Most clients the presence registry keeps (V7); a registration beyond it is refused once the ones without a stream were dropped.</summary>
		public const int MaxPresenceClients = 32;

		/// <summary>How long a registration without any open stream is kept before the registry forgets it (V7).</summary>
		public static readonly TimeSpan PresenceIdleTtl = TimeSpan.FromMinutes(10);

		/// <summary>Most directory entries the path autocomplete looks at per request (V8), however many match.</summary>
		public const int MaxPathEntriesExamined = 5000;

		/// <summary>How long an index build that never returned keeps its lease before the autocomplete forgets it and may start another (V8).</summary>
		public static readonly TimeSpan AutocompleteBuildTimeout = TimeSpan.FromMinutes(10);

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

		/// <summary>Kind of a session suggestion (session mode, FR.7.14).</summary>
		public const string SessionKind = "session";

		/// <summary>Insert text of a session suggestion: <c>session "&lt;name&gt;" (&lt;shortid&gt;)</c> (FR.7.14).</summary>
		public const string SessionInsertFormat = "session \"{0}\" ({1})";

		/// <summary>Separator of the project label and the group path in the detail of a session suggestion (FR.7.14).</summary>
		public const string SessionDetailSeparator = " / ";

		/// <summary>Session suggestions of other projects are added only while the same-project tiers gave fewer results than this (FR.7.14).</summary>
		public const int SessionOtherProjectsBelow = 10;
	}
}
