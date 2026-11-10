using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Looks inside this running process: thread stacks, heap contents, full dumps. Every method is safe to call from any
/// thread except the UI thread, never throws, and never runs two captures at once (a second caller is turned away).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IRuntimeInspector
{
	/// <summary>All managed thread stacks: the UI thread in full, the others truncated.</summary>
	ThreadStackCaptureModel CaptureThreadStacks();

	/// <summary>Writes the top types by bytes and by count, plus totals per GC segment kind, to <paramref name="path"/>.</summary>
	InspectionOutcomeModel WriteHeapHistogram(string path);

	/// <summary>Writes a full dump to <paramref name="path"/>; refused unless <c>DumpsEnabled</c>.</summary>
	InspectionOutcomeModel WriteDump(string path);
}
