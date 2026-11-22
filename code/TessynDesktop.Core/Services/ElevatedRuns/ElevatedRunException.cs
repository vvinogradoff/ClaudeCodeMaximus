using System;

namespace TessynDesktop.Services;

/// <summary>
/// A refused action on an elevated run (FR.15.18): the request is unknown (<see cref="IsUnknown"/>, TDCP -32602) or not in the status the action needs
/// (TDCP -32000). <see cref="Exception.Message"/> is the user-facing text.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunException(string message, bool isUnknown = false) : Exception(message)
{
	public bool IsUnknown { get; } = isUnknown;
}
