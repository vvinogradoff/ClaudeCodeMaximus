using System;
using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Timer-based service that fires scheduled turns (FR.14).
/// Schedules are persisted in AppSettingsModel.Schedules and survive restarts.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISchedulerService
{
	/// <summary>Raised whenever a schedule is added, removed, or fires. Used to refresh session UI.</summary>
	event EventHandler? ScheduleChanged;

	/// <summary>Starts the polling timer. Checks missed fires on startup.</summary>
	void Start();

	/// <summary>Stops the timer.</summary>
	void Stop();

	/// <summary>Adds a schedule, persists immediately, and wakes the timer if needed.</summary>
	void AddSchedule(ScheduleModel schedule);

	/// <summary>
	/// Hands an async worker's result to its supervisor as a Mailbox Delivery (FR.15.3, FR.15.12): appends it to
	/// the supervisor's pending delivery if there is one, otherwise adds a new delay-0 delivery.
	/// </summary>
	void AddOrMergeMailboxDelivery(string supervisorNodeId, string workerName, string resultText);

	/// <summary>
	/// Hands the outcome of an elevated run to the requesting session as a delivery with its own header (FR.15.19): merged with the session's pending delivery when
	/// there is one, held while the session has a turn in progress, exactly as worker results are (FR.15.12).
	/// </summary>
	void AddOrMergeElevatedRunDelivery(string targetNodeId, string runId, string outcomeText);

	/// <summary>Removes a schedule by ID. Returns true if found and removed.</summary>
	bool RemoveSchedule(string scheduleId);

	/// <summary>
	/// Returns all schedules. When <paramref name="targetNodeId"/> is non-null,
	/// returns only schedules targeting that node.
	/// </summary>
	IReadOnlyList<ScheduleModel> GetSchedules(string? targetNodeId = null);
}
