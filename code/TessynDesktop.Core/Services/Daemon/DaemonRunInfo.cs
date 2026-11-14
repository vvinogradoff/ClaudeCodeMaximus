using System;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IDaemonRunInfo" />
/// <remarks>Created by Claude</remarks>
public sealed class DaemonRunInfo : IDaemonRunInfo
{
	/// <inheritdoc />
	public string RunId { get; } = Guid.NewGuid().ToString("N");

	/// <inheritdoc />
	public int ProcessId { get; } = Environment.ProcessId;
}
