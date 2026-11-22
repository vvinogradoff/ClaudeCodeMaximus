using System;

namespace TessynDesktop.Models;

/// <summary>Raised after every status change of an elevated run (tdcp-spec 10.A13 <c>elevated.changed</c>).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunChangedEventArgs(ElevatedRunModel run) : EventArgs
{
	public string RequestId { get; } = run.Id;
	public string NodeId { get; } = run.RequesterNodeId;
	public ElevatedRunStatus Status { get; } = run.Status;
}
