namespace TessynDesktop.Models;

/// <summary>What the UI-hang watchdog sees right now.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record UiWatchdogStateModel(
	bool IsRunning,
	bool StalledNow,
	double CurrentStallMs,
	UiStallRecordModel? LastStall,
	string? CurrentUiOperation,
	int? UiThreadId);
