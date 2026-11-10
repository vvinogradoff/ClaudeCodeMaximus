using System;

namespace TessynDesktop.Models;

/// <summary>One sample of process memory, GC, handle, thread and CPU figures.</summary>
/// <param name="CpuPercent">CPU used over the sampling interval; 100 means one full core.</param>
/// <param name="NativeEstimateMb">Private memory minus GC committed memory: what is not managed heap.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ProcessMetricsSnapshotModel(
	DateTimeOffset TakenAt,
	double PrivateMb,
	double WorkingSetMb,
	double GcHeapMb,
	double GcCommittedMb,
	double GcFragmentedMb,
	double LohMb,
	int Gen0Collections,
	int Gen1Collections,
	int Gen2Collections,
	double GcPausePercent,
	double GcTotalPauseMs,
	double NativeEstimateMb,
	int Handles,
	int Threads,
	int ThreadPoolThreads,
	long ThreadPoolPendingWork,
	double CpuPercent);
