using System;

namespace TessynDesktop.Models;

/// <summary>Timing knobs of the relay connector; the defaults are the contract values, tests shrink them.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record RelayConnectorTimings(
	TimeSpan DeadLinkTimeout,
	TimeSpan EventBatchWindow,
	TimeSpan BackoffResetAfter,
	TimeSpan[] ReconnectBackoff)
{
	public static RelayConnectorTimings Default { get; } = new(
		Constants.Remote.Link.DeadLinkTimeout,
		Constants.Remote.Link.EventBatchWindow,
		Constants.Remote.Link.BackoffResetAfter,
		Constants.Remote.Link.ReconnectBackoff);
}
