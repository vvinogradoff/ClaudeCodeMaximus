using System;
using System.Collections.Generic;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

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

	/// <summary>Removes a schedule by ID. Returns true if found and removed.</summary>
	bool RemoveSchedule(string scheduleId);

	/// <summary>
	/// Returns all schedules. When <paramref name="targetNodeId"/> is non-null,
	/// returns only schedules targeting that node.
	/// </summary>
	IReadOnlyList<ScheduleModel> GetSchedules(string? targetNodeId = null);
}
