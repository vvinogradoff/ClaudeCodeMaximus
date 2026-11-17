using System;
using TessynDesktop.Services;

namespace TessynDesktop;

/// <summary>Entry point of the headless daemon host (<c>TessynDesktop.exe --background</c>). The desktop window is gone; WPF is the only client (A47).</summary>
sealed class Program
{
	[STAThread]
	public static int Main(string[] args)
	{
		// One daemon host per user session (A23 Q6): a WPF-started host and any other start share this guard, so two of them never run side by side
		// and fight over the port and daemon.json. A second start never takes over from the first; it ends quietly.
		using var guard = new DaemonHostGuard();
		if (!guard.TryAcquire())
			return 0;

		// A start without --background (an old shortcut) behaves as --background; the host logs a note about it.
		var options = BackgroundHostOptions.Parse(args);
		return new DaemonHost(startedWithoutBackgroundFlag: !options.IsBackground).Run();
	}
}
