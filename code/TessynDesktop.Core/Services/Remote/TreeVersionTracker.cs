using System.Threading;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ITreeVersionTracker" />
/// <remarks>Created by Claude</remarks>
public sealed class TreeVersionTracker : ITreeVersionTracker
{
	private long _version;

	/// <inheritdoc />
	public long Current => Interlocked.Read(ref _version);

	/// <inheritdoc />
	public long Increment() => Interlocked.Increment(ref _version);
}
