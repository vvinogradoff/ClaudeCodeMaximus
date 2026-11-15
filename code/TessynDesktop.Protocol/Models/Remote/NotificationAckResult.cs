namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>notification.ack</c>.</summary>
/// <param name="Acknowledged">True when the host was still waiting for it (a late or repeated ack is false and harmless).</param>
/// <remarks>Created by Claude</remarks>
public sealed record NotificationAckResult(bool Acknowledged);
