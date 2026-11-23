using System;

namespace TessynDesktop.Models;

/// <summary>The content of <c>result.json</c>, written last by the wrapper (tdcp-spec 10.A13). <see cref="Ended"/> is <c>exited</c>, <c>timedOut</c> or <c>stopped</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunResultFileModel
{
	public int ExitCode { get; set; }
	public DateTimeOffset EndedUtc { get; set; }
	public string Ended { get; set; } = string.Empty;
}
