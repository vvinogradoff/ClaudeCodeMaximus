using System;

namespace TessynDesktop.Models;

/// <summary>The content of <c>started.json</c> (tdcp-spec 10.A13). <see cref="Pid"/> is the wrapper's process; <see cref="CommandPid"/> the started <c>cmd.exe</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunStartedFileModel
{
	public int Pid { get; set; }
	public int? CommandPid { get; set; }
	public DateTimeOffset StartedUtc { get; set; }
}
