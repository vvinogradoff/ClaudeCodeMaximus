using System;

namespace TessynDesktop.Services;

/// <summary>
/// What clients that present notifications have acknowledged (V3). A client that shows a toast (or plays the completion sound) says so with
/// <c>notification.ack</c>; the host stays silent only for what was acknowledged, so a hung or killed client never loses a notification.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface INotificationAckBook
{
	/// <summary>
	/// Starts waiting for the acknowledgement of toast <paramref name="notificationId"/>. Call it before the notification is published, so an
	/// instant acknowledgement is not lost; dispose the handle when done.
	/// </summary>
	IToastAckWait Expect(string notificationId);

	/// <summary>A client showed toast <paramref name="notificationId"/>. False when nothing waits for it (unknown or already given up).</summary>
	bool AcknowledgeToast(string notificationId);

	/// <summary>A client played the completion sound of session <paramref name="nodeId"/>.</summary>
	void AcknowledgeSound(string nodeId);

	/// <summary>True (once) when the completion sound of <paramref name="nodeId"/> was acknowledged at or after <paramref name="since"/>.</summary>
	bool TryConsumeSoundAck(string nodeId, DateTimeOffset since);
}
