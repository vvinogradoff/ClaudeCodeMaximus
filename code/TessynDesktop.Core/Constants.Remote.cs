using System;

namespace TessynDesktop;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Names and timings of the remote (web client) API: the contract in .web-work/10-contract.md.</summary>
	public static class Remote
	{
		/// <summary>Version reported by <c>client.hello</c>; bumped only for a breaking wire change.</summary>
		public const int ProtocolVersion = 1;

		/// <summary>JSON-RPC method names that may cross the relay (contract section 3).</summary>
		public static class Methods
		{
			public const string ClientHello = "client.hello";
			public const string TreeSnapshot = "tree.snapshot";
			public const string TreeWorkers = "tree.workers";
			public const string TreeSearch = "tree.search";
			public const string TreeCreateSession = "tree.createSession";
			public const string RecentList = "recent.list";
			public const string RecentHide = "recent.hide";
			public const string TagsSearch = "tags.search";
			public const string SessionGet = "session.get";
			public const string SessionEntries = "session.entries";
			public const string SessionEntry = "session.entry";
			public const string SessionSend = "session.send";
			public const string SessionStop = "session.stop";
			public const string SessionCompact = "session.compact";
			public const string SessionSetFavorite = "session.setFavorite";
			public const string SessionSetStreaming = "session.setStreaming";
			public const string AttentionList = "attention.list";
			public const string AttentionReply = "attention.reply";
			public const string AttentionDismiss = "attention.dismiss";
			public const string CatalogGet = "catalog.get";
			public const string UsageGet = "usage.get";
		}

		/// <summary>Notification names published on the remote event bus (contract section 4).</summary>
		public static class Events
		{
			public const string TreeChanged = "tree.changed";
			public const string SessionChanged = "session.changed";
			public const string SessionAppended = "session.appended";
			public const string SessionReset = "session.reset";
			public const string TurnCompleted = "turn.completed";
			public const string AttentionChanged = "attention.changed";
			public const string TagsChanged = "tags.changed";
			public const string UsageChanged = "usage.changed";
			public const string IndexChanged = "index.changed";
		}

		/// <summary>Debounce windows of the notification publisher (contract section 4).</summary>
		public static class Timings
		{
			public static readonly TimeSpan TreeChangedWindow = TimeSpan.FromMilliseconds(300);
			public static readonly TimeSpan SessionChangedWindow = TimeSpan.FromMilliseconds(250);
			public static readonly TimeSpan SessionAppendedWindow = TimeSpan.FromMilliseconds(250);
			public static readonly TimeSpan AttentionChangedWindow = TimeSpan.FromMilliseconds(250);
			public static readonly TimeSpan TagsChangedWindow = TimeSpan.FromMilliseconds(250);
			public static readonly TimeSpan IndexChangedWindow = TimeSpan.FromSeconds(2);

			/// <summary>Window for notifications that carry no debounce of their own (turn.completed, session.reset): run the factory right away, off the caller's thread.</summary>
			public static readonly TimeSpan Immediate = TimeSpan.Zero;
		}

		/// <summary>Relay link (contract section 6) — consumed by the relay connector work package.</summary>
		public static class Link
		{
			public const string StreamPath = "/link/stream";
			public const string ReplyPath = "/link/reply";
			public const string EventsPath = "/link/events";
			public const string DaemonHeader = "X-Tessyn-Daemon";
			public const string ProtocolHeader = "X-Tessyn-Protocol";
			public const int DispatchConcurrency = 8;

			/// <summary>File name of the DPAPI-protected relay config under %AppData%\TessynDesktop (contract section 6).</summary>
			public const string ConfigFileName = "web-relay.dat";

			/// <summary>Protocol version sent in <see cref="ProtocolHeader"/>.</summary>
			public const string ProtocolHeaderValue = "1";

			/// <summary>Shown as the connector's error when the server answers 401.</summary>
			public const string TokenRejectedMessage = "Link token rejected";

			/// <summary>How long Stop() waits for the connector loop to finish.</summary>
			public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

			/// <summary>Per-request timeout of the reply and event POSTs.</summary>
			public static readonly TimeSpan PostTimeout = TimeSpan.FromSeconds(30);

			/// <summary>Capacity of the outbound event queue; the oldest events are dropped beyond it.</summary>
			public const int EventQueueCapacity = 5000;
			public static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(15);
			public static readonly TimeSpan DeadLinkTimeout = TimeSpan.FromSeconds(45);
			public static readonly TimeSpan EventBatchWindow = TimeSpan.FromMilliseconds(250);
			public static readonly TimeSpan BackoffResetAfter = TimeSpan.FromSeconds(60);
			public static readonly TimeSpan[] ReconnectBackoff =
			[
				TimeSpan.FromSeconds(1),
				TimeSpan.FromSeconds(2),
				TimeSpan.FromSeconds(5),
				TimeSpan.FromSeconds(10),
				TimeSpan.FromSeconds(30),
			];
		}

		/// <summary>Limits of the session-content methods (contract sections 3.10 and 3.20).</summary>
		public static class Limits
		{
			public const int EntryContentMaxChars = 32_768;
			public const int EntriesDefaultLimit = 50;
			public const int EntriesMaxLimit = 200;
		}

		/// <summary>Per-model effort values offered by <c>catalog.get</c>; the first is "no flag".</summary>
		public static readonly string[] Efforts = ["default", "max", "high", "medium", "low"];

		/// <summary>Id of the "no explicit choice" model and profile entries of the catalog.</summary>
		public const string DefaultOptionId = "default";

		/// <summary>How much of a session file's end the activity service parses (contract section 3, SessionActivityService).</summary>
		public const int ActivityTailBytes = 64 * 1024;
	}
}
