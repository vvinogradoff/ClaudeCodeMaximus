namespace TessynDesktop.WPF.Switch;

/// <summary>Tells whether the daemon's source changed between the running daemon and the newest build (FR.32.9).</summary>
/// <remarks>Created by Claude</remarks>
public interface IDaemonChangeDetector
{
	DaemonChangeResult Evaluate(string runningFolder, string buildFolder, string sourceLocation);
}
