using System;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>Samples process memory, GC, thread and CPU figures on a background timer.</summary>
/// <remarks>Created by Claude</remarks>
public interface IProcessMetricsSampler : IDisposable
{
	/// <summary>Starts the periodic background sampling; does nothing if already started.</summary>
	void Start();

	/// <summary>Takes a fresh sample now, on the calling thread. Safe from any thread.</summary>
	ProcessMetricsSnapshotModel GetSnapshot();
}
