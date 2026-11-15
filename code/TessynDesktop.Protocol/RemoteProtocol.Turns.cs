namespace TessynDesktop.Models.Remote;

public static partial class RemoteProtocol
{
	/// <summary>
	/// The interactive turn stream (P1.I): two loopback-only methods and the notifications of a running turn, announced by the
	/// <see cref="Capability"/> flag of <c>client.hello</c>. The notifications are additive and local: the relay never carries them.
	/// </summary>
	public static class TurnStream
	{
		/// <summary>The daemon runs interactive turns and publishes their progress.</summary>
		public const string Capability = "turn.stream";

		/// <summary><c>turn.send { nodeId, text, modelId?, effort?, profileId?, clientRequestId? }</c> answers <c>{ turnId, accepted, queued }</c>. Loopback only.</summary>
		public const string SendMethod = "turn.send";

		/// <summary><c>turn.stop { nodeId }</c> answers <c>{ stopped }</c>. Loopback only.</summary>
		public const string StopMethod = "turn.stop";

		public const string StartedEvent = "turn.started";
		public const string DeltaEvent = "turn.delta";
		public const string ToolEvent = "turn.tool";
		public const string StatusEvent = "turn.status";
		public const string UsageEvent = "turn.usage";

		/// <summary>Reason of a <c>turn.status</c> retry: the conversation was not found and the turn is re-sent with its history.</summary>
		public const string RetryContext = "context";

		/// <summary>Reason of a <c>turn.status</c> retry: the transient OAuth refresh race (ISSUE.010).</summary>
		public const string RetryAuth = "auth";

		/// <summary>The notifications that only the local stream carries (the relay filters them out).</summary>
		public static readonly IReadOnlyList<string> LocalOnlyEvents = [StartedEvent, DeltaEvent, ToolEvent, StatusEvent, UsageEvent];

		/// <summary>
		/// True for a notification the relay must not carry: the five turn-stream events, and the <c>turn.completed</c> of a turn that never
		/// ran (a phone must not see an end for a turn it never saw run: it would clear its pending state while another turn still runs).
		/// </summary>
		public static bool IsLocalOnly(RemoteNotification notification) =>
			LocalOnlyEvents.Contains(notification.Method)
			|| (notification.Method == Events.TurnCompleted && notification.Params["neverRan"]?.GetValue<bool>() == true);
	}
}
