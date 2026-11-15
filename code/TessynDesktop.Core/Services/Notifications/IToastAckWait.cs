using System;

namespace TessynDesktop.Services;

/// <summary>A wait for the acknowledgement of one toast; see <see cref="INotificationAckBook.Expect"/>.</summary>
/// <remarks>Created by Claude</remarks>
public interface IToastAckWait : IDisposable
{
	/// <summary>Blocks up to <paramref name="timeout"/>; true when the toast was acknowledged.</summary>
	bool Wait(TimeSpan timeout);
}
