namespace TessynDesktop.Models;

/// <summary>Settings for the always-on runtime diagnostics (<c>Diagnostics</c> section of appsettings.json).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DiagnosticsSettingsModel
{
	/// <summary>Master switch for the metrics sampler and the UI-hang watchdog. Read once at startup.</summary>
	public bool Enabled { get; set; } = true;

	/// <summary>Log at Debug instead of Information. Read once at startup.</summary>
	public bool VerboseLogging { get; set; }

	/// <summary>UI stall length that logs a warning.</summary>
	public int HangWarnMs { get; set; } = 1000;

	/// <summary>UI stall length that captures all thread stacks to the diag folder.</summary>
	public int HangCaptureMs { get; set; } = 5000;

	/// <summary>Minimum minutes between two hang captures.</summary>
	public int HangCaptureCooldownMinutes { get; set; } = 10;

	/// <summary>Seconds between process metrics samples (the <c>MEM</c> log line).</summary>
	public int MetricsIntervalSeconds { get; set; } = 60;

	/// <summary>Private memory (MB) at which a heap histogram is captured.</summary>
	public int MemorySpikeThresholdMb { get; set; } = 4096;

	/// <summary>Allows <c>diag_dump</c> to write full process dumps (they contain session content).</summary>
	public bool DumpsEnabled { get; set; }

	/// <summary>Text artifacts (hang stacks, heap histograms) kept per kind.</summary>
	public int ArtifactRetentionCount { get; set; } = 20;

	/// <summary>Process dumps kept.</summary>
	public int DumpRetentionCount { get; set; } = 3;
}
