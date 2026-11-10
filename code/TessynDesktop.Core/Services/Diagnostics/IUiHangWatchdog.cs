using System;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>Detects UI-thread stalls from a dedicated background thread and records what the UI was doing.</summary>
/// <remarks>Created by Claude</remarks>
public interface IUiHangWatchdog : IDisposable
{
	/// <summary>Starts the watchdog thread; does nothing if already started.</summary>
	void Start();

	/// <summary>Current stall state. Reads volatile fields only, so it answers while the UI is frozen.</summary>
	UiWatchdogStateModel GetState();
}
