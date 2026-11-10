namespace TessynDesktop.Services;

/// <summary>Times UI operations: warns when one runs long and tells the watchdog which one is current.</summary>
/// <remarks>Created by Claude</remarks>
public interface IOperationTimer
{
	/// <summary>Name of the innermost UI-thread operation in progress, or null.</summary>
	string? CurrentOperation { get; }

	/// <summary>Starts timing <paramref name="operation"/>; dispose the scope to stop. Logs a warning when it took at least <paramref name="warnMs"/>.</summary>
	OperationScope Measure(string operation, int warnMs);

	/// <summary>Called by <see cref="OperationScope.Dispose"/>.</summary>
	void Complete(in OperationScope scope);
}
