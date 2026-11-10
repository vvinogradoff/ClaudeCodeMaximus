namespace TessynDesktop.Models;

/// <summary>Response of the <c>diag_snapshot</c> MCP tool.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record DiagnosticsSnapshotModel(
	ProcessMetricsSnapshotModel Metrics,
	UiWatchdogStateModel Watchdog);
