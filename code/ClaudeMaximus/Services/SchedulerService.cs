using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClaudeMaximus.Models;
using ClaudeMaximus.ViewModels;
using Serilog;

namespace ClaudeMaximus.Services;

/// <summary>
/// Polls every 30 s; fires any schedule whose <c>FireAtUtc</c> has passed.
/// Cron schedules re-arm immediately after firing by advancing <c>FireAtUtc</c>.
/// Missed-fire handling (default: FireOnce) runs once on <see cref="Start"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SchedulerService : ISchedulerService
{
	private static readonly ILogger _log = Log.ForContext<SchedulerService>();
	private const int PollIntervalMs = 30_000;

	private readonly IAppSettingsService    _appSettings;
	private readonly ISessionTurnService    _turnService;
	private readonly SessionTreeViewModel   _sessionTree;
	private readonly INotificationService   _notifications;

	private Timer? _timer;
	private readonly object _lock = new();

	public SchedulerService(
		IAppSettingsService appSettings,
		ISessionTurnService turnService,
		SessionTreeViewModel sessionTree,
		INotificationService notifications)
	{
		_appSettings   = appSettings;
		_turnService   = turnService;
		_sessionTree   = sessionTree;
		_notifications = notifications;
	}

	public void Start()
	{
		HandleMissedFires();
		_timer = new Timer(_ => Poll(), null, 0, PollIntervalMs);
		_log.Information("SchedulerService started. Active schedules: {Count}", _appSettings.Settings.Schedules.Count);
	}

	public void Stop()
	{
		_timer?.Dispose();
		_timer = null;
	}

	public void AddSchedule(ScheduleModel schedule)
	{
		// For cron, compute the first FireAtUtc from 'now'.
		if (schedule.Kind == ScheduleKind.Cron && schedule.FireAtUtc == null)
			schedule.FireAtUtc = CronHelper.GetNextOccurrence(schedule.CronExpression!, DateTimeOffset.UtcNow);

		lock (_lock)
			_appSettings.Settings.Schedules.Add(schedule);
		_appSettings.Save();

		_log.Information("Schedule added: {Id} kind={Kind} target={Target} fireAt={At}",
			schedule.ScheduleId, schedule.Kind, schedule.TargetNodeId, schedule.FireAtUtc);
	}

	public bool RemoveSchedule(string scheduleId)
	{
		ScheduleModel? found;
		lock (_lock)
		{
			found = _appSettings.Settings.Schedules.FirstOrDefault(s => s.ScheduleId == scheduleId);
			if (found == null)
				return false;
			_appSettings.Settings.Schedules.Remove(found);
		}
		_appSettings.Save();
		_log.Information("Schedule cancelled: {Id}", scheduleId);
		return true;
	}

	public IReadOnlyList<ScheduleModel> GetSchedules(string? targetNodeId = null)
	{
		lock (_lock)
		{
			var list = _appSettings.Settings.Schedules.AsEnumerable();
			if (targetNodeId != null)
				list = list.Where(s => s.TargetNodeId == targetNodeId);
			return list.ToList();
		}
	}

	// ── Private ───────────────────────────────────────────────────────────────

	private void HandleMissedFires()
	{
		var now = DateTimeOffset.UtcNow;
		List<ScheduleModel> missed;
		lock (_lock)
			missed = _appSettings.Settings.Schedules
				.Where(s => s.FireAtUtc.HasValue && s.FireAtUtc.Value <= now)
				.ToList();

		foreach (var s in missed)
		{
			switch (s.MissedFirePolicy)
			{
				case MissedFirePolicy.Skip:
					if (s.Kind == ScheduleKind.Cron)
					{
						// Advance to next future occurrence without firing.
						s.FireAtUtc = CronHelper.GetNextOccurrence(s.CronExpression!, now);
						_log.Information("Missed cron fire skipped, advanced: {Id} → {At}", s.ScheduleId, s.FireAtUtc);
					}
					else
					{
						lock (_lock)
							_appSettings.Settings.Schedules.Remove(s);
						_log.Information("Missed one-shot fire skipped and removed: {Id}", s.ScheduleId);
					}
					break;

				default: // FireOnce
					_log.Information("Firing missed schedule on startup: {Id}", s.ScheduleId);
					_ = FireScheduleAsync(s);
					break;
			}
		}

		_appSettings.Save();
	}

	private void Poll()
	{
		var now = DateTimeOffset.UtcNow;
		List<ScheduleModel> due;
		lock (_lock)
			due = _appSettings.Settings.Schedules
				.Where(s => s.FireAtUtc.HasValue && s.FireAtUtc.Value <= now)
				.ToList();

		foreach (var s in due)
			_ = FireScheduleAsync(s);
	}

	private async Task FireScheduleAsync(ScheduleModel schedule)
	{
		_log.Information("Firing schedule {Id} → node {Target}", schedule.ScheduleId, schedule.TargetNodeId);

		// Resolve target node on UI thread.
		SessionNodeModel? targetNode = null;
		await Dispatcher.UIThread.InvokeAsync(() =>
			targetNode = _sessionTree.FindModelByNodeId(schedule.TargetNodeId));

		if (targetNode == null)
		{
			_log.Warning("Schedule {Id}: target node {Target} not found — removing", schedule.ScheduleId, schedule.TargetNodeId);
			RemoveSchedule(schedule.ScheduleId);
			return;
		}

		// Advance / remove the schedule before firing to prevent double-fire if the turn is slow.
		bool shouldRemove;
		lock (_lock)
		{
			schedule.FireCount++;
			if (schedule.Kind == ScheduleKind.Cron)
			{
				shouldRemove = schedule.MaxFires > 0 && schedule.FireCount >= schedule.MaxFires;
				if (!shouldRemove)
					schedule.FireAtUtc = CronHelper.GetNextOccurrence(schedule.CronExpression!, DateTimeOffset.UtcNow);
				else
					_appSettings.Settings.Schedules.Remove(schedule);
			}
			else
			{
				shouldRemove = true;
				_appSettings.Settings.Schedules.Remove(schedule);
			}
		}
		_appSettings.Save();

		// Build the prompt — prefix with note if available.
		var prompt = string.IsNullOrEmpty(schedule.Note)
			? schedule.Prompt
			: $"{Constants.Agent.ScheduledTurnSystemPrefix} {schedule.Note}\n{schedule.Prompt}";

		TurnResultModel? result = null;
		using var cts = new CancellationTokenSource(Constants.Agent.ScheduledTurnTimeoutMs);
		try
		{
			result = await _turnService.RunTurnAsync(targetNode, prompt, TurnSource.Scheduled, cts.Token);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "SchedulerService: turn failed for schedule {Id}", schedule.ScheduleId);
		}

		if (result != null && !result.IsError && !string.IsNullOrWhiteSpace(result.AssistantText))
		{
			bool isSelected = false;
			await Dispatcher.UIThread.InvokeAsync(() =>
				isSelected = _sessionTree.SelectedSession?.Model.NodeId == targetNode.NodeId);

			if (!isSelected)
				_notifications.ShowResult(targetNode.NodeId, targetNode.Name, result.AssistantText, schedule.Note);
		}

		if (!shouldRemove)
			_log.Information("Cron schedule {Id} re-armed for {At}", schedule.ScheduleId, schedule.FireAtUtc);
	}
}

// ── Minimal cron helper ───────────────────────────────────────────────────────

/// <summary>
/// Minimal 5-field cron parser (minute hour DOM month DOW).
/// Supports: * (any), number, */n (step), a-b (range), a,b,c (list).
/// </summary>
file static class CronHelper
{
	/// <summary>Returns the next occurrence after <paramref name="after"/>, aligned to whole minutes.</summary>
	public static DateTimeOffset GetNextOccurrence(string expression, DateTimeOffset after)
	{
		var parts = expression.Trim().Split(' ');
		if (parts.Length != 5)
			throw new ArgumentException($"Invalid cron expression (expected 5 fields): '{expression}'");

		var minuteSet = Expand(parts[0], 0, 59);
		var hourSet   = Expand(parts[1], 0, 23);
		var domSet    = Expand(parts[2], 1, 31);
		var monthSet  = Expand(parts[3], 1, 12);
		var dowSet    = Expand(parts[4], 0, 6);

		// Start one minute after 'after', aligned to the minute boundary.
		var candidate = new DateTimeOffset(
			after.UtcDateTime.Year, after.UtcDateTime.Month, after.UtcDateTime.Day,
			after.UtcDateTime.Hour, after.UtcDateTime.Minute, 0, TimeSpan.Zero)
			.AddMinutes(1);

		// Scan up to 2 years to find the next match.
		var limit = after.AddYears(2);
		while (candidate < limit)
		{
			if (!monthSet.Contains(candidate.Month))     { candidate = candidate.AddMonths(1); candidate = AlignToMonth(candidate); continue; }
			if (!domSet.Contains(candidate.Day))         { candidate = candidate.AddDays(1);   candidate = AlignToDay(candidate);   continue; }
			if (!dowSet.Contains((int)candidate.DayOfWeek)) { candidate = candidate.AddDays(1); candidate = AlignToDay(candidate);   continue; }
			if (!hourSet.Contains(candidate.Hour))       { candidate = candidate.AddHours(1);  candidate = AlignToHour(candidate);  continue; }
			if (!minuteSet.Contains(candidate.Minute))   { candidate = candidate.AddMinutes(1); continue; }
			return candidate;
		}
		throw new InvalidOperationException($"Cron expression '{expression}' has no occurrence in the next 2 years.");
	}

	private static DateTimeOffset AlignToMonth(DateTimeOffset dt) =>
		new(dt.Year, dt.Month, 1, 0, 0, 0, TimeSpan.Zero);
	private static DateTimeOffset AlignToDay(DateTimeOffset dt) =>
		new(dt.Year, dt.Month, dt.Day, 0, 0, 0, TimeSpan.Zero);
	private static DateTimeOffset AlignToHour(DateTimeOffset dt) =>
		new(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, TimeSpan.Zero);

	private static HashSet<int> Expand(string field, int min, int max)
	{
		var set = new HashSet<int>();
		foreach (var part in field.Split(','))
		{
			if (part == "*")
			{ for (var i = min; i <= max; i++) set.Add(i); }
			else if (part.StartsWith("*/"))
			{
				var step = int.Parse(part[2..]);
				for (var i = min; i <= max; i += step) set.Add(i);
			}
			else if (part.Contains('-'))
			{
				var dash = part.IndexOf('-');
				var lo   = int.Parse(part[..dash]);
				var rest = part[(dash + 1)..];
				int hi, step2 = 1;
				if (rest.Contains('/'))
				{
					var slash = rest.IndexOf('/');
					hi    = int.Parse(rest[..slash]);
					step2 = int.Parse(rest[(slash + 1)..]);
				}
				else
					hi = int.Parse(rest);
				for (var i = lo; i <= hi; i += step2) set.Add(i);
			}
			else
				set.Add(int.Parse(part));
		}
		return set;
	}
}
