using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Serilog;

namespace TessynDesktop.Services;

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
	private readonly ISessionRegistry       _registry;

	private Timer? _timer;
	private readonly object _lock = new();

	public event EventHandler? ScheduleChanged;

	public SchedulerService(
		IAppSettingsService appSettings,
		ISessionTurnService turnService,
		ISessionRegistry registry)
	{
		_appSettings   = appSettings;
		_turnService   = turnService;
		_registry      = registry;
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
		ScheduleChanged?.Invoke(this, EventArgs.Empty);
	}

	public void AddOrMergeMailboxDelivery(string supervisorNodeId, string workerName, string resultText)
	{
		var block = $"{Constants.Agent.WorkerFinishedPrefix} {workerName}\n{resultText}";
		ScheduleModel schedule;

		// The merge and Poll/FireScheduleAsync's removal both run under _lock, so a result either lands in a
		// delivery that has not been claimed yet or finds none and starts a new one — never lost, never twice.
		lock (_lock)
		{
			var pending = _appSettings.Settings.Schedules.FirstOrDefault(
				s => s.IsMailboxDelivery && s.TargetNodeId == supervisorNodeId);
			if (pending != null)
			{
				pending.Prompt = $"{pending.Prompt}\n\n{block}";
				pending.Note   = BuildMergedNote(pending.Note, workerName);
				schedule = pending;
			}
			else
			{
				schedule = new ScheduleModel
				{
					ScheduleId        = Guid.NewGuid().ToString("N")[..8],
					TargetNodeId      = supervisorNodeId,
					Prompt            = block,
					Note              = string.Format(Constants.Agent.MailboxSingleNoteFormat, workerName),
					Kind              = ScheduleKind.Delay,
					FireAtUtc         = DateTimeOffset.UtcNow,
					IsMailboxDelivery = true,
				};
				_appSettings.Settings.Schedules.Add(schedule);
			}
		}
		_appSettings.Save();

		_log.Information("Mailbox delivery {Id} for supervisor {Target}: {Note}",
			schedule.ScheduleId, supervisorNodeId, schedule.Note);
		ScheduleChanged?.Invoke(this, EventArgs.Empty);
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
		ScheduleChanged?.Invoke(this, EventArgs.Empty);
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

	private static readonly Regex _singleNote = new(
		"^Worker '(?<name>.*)' result$", RegexOptions.Singleline | RegexOptions.Compiled);
	private static readonly Regex _mergedNote = new(
		@"^Worker results \((?<n>\d+)\): (?<names>.*)$", RegexOptions.Singleline | RegexOptions.Compiled);

	/// <summary>Builds the note of a mailbox delivery after one more worker result was merged into it (FR.15.12).</summary>
	private static string BuildMergedNote(string currentNote, string workerName)
	{
		var merged = _mergedNote.Match(currentNote);
		if (merged.Success)
			return string.Format(Constants.Agent.MailboxMergedNoteFormat,
				int.Parse(merged.Groups["n"].Value) + 1, $"{merged.Groups["names"].Value}, {workerName}");

		var single = _singleNote.Match(currentNote);
		var first  = single.Success ? single.Groups["name"].Value : currentNote;
		return string.Format(Constants.Agent.MailboxMergedNoteFormat, 2, $"{first}, {workerName}");
	}

	internal void Poll()
	{
		var now = DateTimeOffset.UtcNow;
		List<ScheduleModel> due;
		lock (_lock)
			due = _appSettings.Settings.Schedules
				.Where(s => s.FireAtUtc.HasValue && s.FireAtUtc.Value <= now)
				// FR.14.9, FR.15.12: a Mailbox Delivery stays pending while its target has a turn in progress,
				// so results that keep arriving merge into it instead of each queueing on the turn lock.
				.Where(s => !(s.IsMailboxDelivery && _turnService.GetTurnLock(s.TargetNodeId).CurrentCount == 0))
				.ToList();

		foreach (var s in due)
			_ = FireScheduleAsync(s);
	}

	private async Task FireScheduleAsync(ScheduleModel schedule)
	{
		_log.Information("Firing schedule {Id} → node {Target}", schedule.ScheduleId, schedule.TargetNodeId);

		// Resolve target node via the registry (thread-safe, no UI dispatch needed).
		var targetNode = _registry.FindModelByNodeId(schedule.TargetNodeId);

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
				// A mailbox delivery already claimed by another fire must not be delivered twice.
				if (!_appSettings.Settings.Schedules.Remove(schedule) && schedule.IsMailboxDelivery)
					return;
			}
		}
		_appSettings.Save();
		ScheduleChanged?.Invoke(this, EventArgs.Empty);

		// Build the prompt — prefix with note if available.
		var prompt = string.IsNullOrEmpty(schedule.Note)
			? schedule.Prompt
			: $"{Constants.Agent.ScheduledTurnSystemPrefix} {schedule.Note}\n{schedule.Prompt}";

		using var cts = new CancellationTokenSource(Constants.Agent.ScheduledTurnTimeoutMs);
		try
		{
			await _turnService.RunTurnAsync(targetNode, prompt, TurnSource.Scheduled, cts.Token);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "SchedulerService: turn failed for schedule {Id}", schedule.ScheduleId);
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
