using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

public static partial class RemoteProtocol
{
	/// <summary>
	/// Elevated runs requested by an agent (tdcp-spec 10.A13, FR.15.16 to FR.15.20). Every method is loopback only (<c>remoteCallable: false</c>);
	/// <c>elevated.changed</c> is a local-stream-only notification. Announced by the <c>elevatedRuns</c> capability of the loopback <c>client.hello</c>.
	/// </summary>
	public static class ElevatedRuns
	{
		public const string Capability = "elevatedRuns";

		public const string ListMethod = "elevated.list";
		public const string GetMethod = "elevated.get";
		public const string ApproveMethod = "elevated.approve";
		public const string LaunchedMethod = "elevated.launched";
		public const string LaunchFailedMethod = "elevated.launchFailed";
		public const string DeclineMethod = "elevated.decline";
		public const string StopMethod = "elevated.stop";

		/// <summary><c>{ requestId, nodeId, status }</c> after every status change. Local stream only.</summary>
		public const string ChangedEvent = "elevated.changed";

		/// <summary>The <see cref="AttentionActionDto.Type"/> of the Focus item of an elevated run.</summary>
		public const string ActionType = "elevatedRun";

		/// <summary>The ShellExecute verb of <see cref="ElevatedLaunchDto.Verb"/>.</summary>
		public const string RunAsVerb = "runas";

		/// <summary>Seconds a client waits for the wrapper's <c>started.json</c> after reporting the launch (<see cref="ElevatedLaunchDto.StartTimeoutSeconds"/>).</summary>
		public const int StartTimeoutSeconds = 60;

		public const string ListActive = "active";
		public const string ListAll = "all";

		/// <summary>Largest <c>tailLines</c> of <c>elevated.get</c>.</summary>
		public const int MaxTailLines = 200;

		/// <summary>Longest line of an output tail.</summary>
		public const int MaxTailLineLength = 500;

		/// <summary>At most this many runs in an <c>elevated.list</c> answer.</summary>
		public const int MaxListed = 100;

		/// <summary>Longest decline note and failure text.</summary>
		public const int MaxTextLength = 500;

		/// <summary>The text of the refusal of a reply or dismiss on an elevated-run item (-32602).</summary>
		public const string ItemRefusalText = "This item is an elevated-run request: use Run as administrator or Decline.";

		/// <summary>Format of the refusal of an approve, decline or launch report; arg 0 = the status name as the wire writes it.</summary>
		public const string NoLongerPendingFormat = "This request is no longer pending ({0}).";

		public const string ExpiredText = "This request expired.";

		public static readonly IReadOnlyList<string> LocalOnlyEvents = [ChangedEvent];
	}
}
