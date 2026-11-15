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
	// No private lock: Settings.Schedules is part of the settings graph that Save() serializes under
	// IAppSettingsService.SyncRoot, so every access must use that same lock (ISSUE.008 class).
	private object _lock => _appSettings.SyncRoot;

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
		_timer = new Timer(_ => PollSafe(), null, 0, PollIntervalMs);
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
		{
			// An invalid expression throws ArgumentException to the caller and nothing is added. An expression that
			// is valid but never matches within the scan window is stored parked (FireAtUtc null: never due).
			var first = CronHelper.TryGetNextOccurrence(schedule.CronExpression!, DateTimeOffset.UtcNow);
			schedule.FireAtUtc = first;
		}

		lock (_lock)
			_appSettings.Settings.Schedules.Add(schedule);
		_appSettings.Save();

		_log.Information("Schedule added: {Id} kind={Kind} target={Target} fireAt={At}",
			schedule.ScheduleId, schedule.Kind, schedule.TargetNodeId, schedule.FireAtUtc);
		ScheduleChanged?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>Posts a note about a scheduled turn to the target's supervisor through the mailbox; nothing for a top-level session or for a mailbox delivery itself (no loop).</summary>
	private void NotifySupervisor(ScheduleModel schedule, SessionNodeModel targetNode, string text)
	{
		if (schedule.IsMailboxDelivery || string.IsNullOrEmpty(targetNode.SupervisorNodeId))
			return;

		try { AddOrMergeMailboxDelivery(targetNode.SupervisorNodeId, targetNode.Name, text); }
		catch (Exception ex) { _log.Warning(ex, "SchedulerService: could not notify the supervisor of {NodeId}", targetNode.NodeId); }
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
			try
			{
				switch (s.MissedFirePolicy)
				{
					case MissedFirePolicy.Skip:
						if (s.Kind == ScheduleKind.Cron)
						{
							// Advance to next future occurrence without firing.
							var (invalid, next) = TryGetNextCronOccurrence(s, now);
							lock (_lock)
							{
								if (invalid)
									_appSettings.Settings.Schedules.Remove(s);
								else
									s.FireAtUtc = next; // null parks a schedule that never matches
							}
							if (!invalid && next != null)
								_log.Information("Missed cron fire skipped, advanced: {Id} → {At}", s.ScheduleId, next);
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
			catch (Exception ex)
			{
				// One bad persisted schedule must not abort startup or the handling of the others.
				_log.Error(ex, "SchedulerService: could not handle missed fire of schedule {Id}", s.ScheduleId);
			}
		}

		_appSettings.Save();
	}

	/// <summary>
	/// The next occurrence of a cron schedule. <c>Invalid</c> is true (and logged) when the expression cannot be
	/// evaluated: such a schedule can never fire correctly, so callers drop it instead of retrying forever.
	/// A valid expression with no match inside the scan window yields <c>Next == null</c> and is NOT invalid.
	/// </summary>
	private (bool Invalid, DateTimeOffset? Next) TryGetNextCronOccurrence(ScheduleModel schedule, DateTimeOffset after)
	{
		try
		{
			var next = CronHelper.TryGetNextOccurrence(schedule.CronExpression!, after);
			if (next == null)
				_log.Warning("Schedule {Id}: cron expression '{Cron}' has no occurrence within {Years} years → parked",
					schedule.ScheduleId, schedule.CronExpression, CronHelper.ScanYears);
			return (false, next);
		}
		catch (Exception ex)
		{
			_log.Error(ex, "Schedule {Id}: cron expression '{Cron}' is not usable → removing the schedule",
				schedule.ScheduleId, schedule.CronExpression);
			return (true, null);
		}
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

	/// <summary>Timer entry point: an exception escaping a thread-pool timer callback would take the whole process down.</summary>
	private void PollSafe()
	{
		try { Poll(); }
		catch (Exception ex) { _log.Error(ex, "SchedulerService: poll failed; will retry on the next tick"); }
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
				{
					var (invalid, next) = TryGetNextCronOccurrence(schedule, DateTimeOffset.UtcNow);
					if (invalid)
						shouldRemove = true; // unusable expression: drop it so it is not retried on every poll
					else
						schedule.FireAtUtc = next; // null parks a schedule that never matches again
				}
				if (shouldRemove)
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
		try
		{
			_appSettings.Save();
			ScheduleChanged?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception ex)
		{
			// The schedule is already advanced/removed in memory; a failed save or a throwing subscriber must
			// not turn this fire-and-forget task into an unobserved fault or skip the turn.
			_log.Warning(ex, "SchedulerService: persisting/announcing schedule {Id} failed", schedule.ScheduleId);
		}

		// Build the prompt — prefix with note if available.
		var prompt = string.IsNullOrEmpty(schedule.Note)
			? schedule.Prompt
			: $"{Constants.Agent.ScheduledTurnSystemPrefix} {schedule.Note}\n{schedule.Prompt}";

		using var cts = new CancellationTokenSource(Constants.Agent.ScheduledTurnTimeoutMs);
		try
		{
			var result = await _turnService.RunTurnAsync(targetNode, prompt, TurnSource.Scheduled, cts.Token);

			// ISSUE.013: a refused turn (its pinned profile does not exist) is logged and reported to the supervisor's mailbox, not lost.
			if (result?.Refused == true)
			{
				_log.Warning("SchedulerService: turn refused for schedule {Id} on node {NodeId}: {Error}", schedule.ScheduleId, targetNode.NodeId, result.ErrorMessage);
				NotifySupervisor(schedule, targetNode, $"Scheduled turn was refused: {result.ErrorMessage}");
			}
			else if (result?.Note != null)
				NotifySupervisor(schedule, targetNode, $"Scheduled turn ran on the Default profile: {result.Note}.");
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
	/// <summary>
	/// How far ahead the scan looks. A 29 February schedule's next match can be 8 years away (2096 → 2104), so
	/// the window must exceed that.
	/// </summary>
	public const int ScanYears = 10;

	/// <summary>
	/// Returns the next occurrence after <paramref name="after"/>, aligned to whole minutes, or null when the
	/// (valid) expression has no match within <see cref="ScanYears"/>. Throws for an invalid expression.
	/// </summary>
	public static DateTimeOffset? TryGetNextOccurrence(string expression, DateTimeOffset after)
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

		var limit = after.AddYears(ScanYears);
		while (candidate < limit)
		{
			if (!monthSet.Contains(candidate.Month))     { candidate = candidate.AddMonths(1); candidate = AlignToMonth(candidate); continue; }
			if (!domSet.Contains(candidate.Day))         { candidate = candidate.AddDays(1);   candidate = AlignToDay(candidate);   continue; }
			if (!dowSet.Contains((int)candidate.DayOfWeek)) { candidate = candidate.AddDays(1); candidate = AlignToDay(candidate);   continue; }
			if (!hourSet.Contains(candidate.Hour))       { candidate = candidate.AddHours(1);  candidate = AlignToHour(candidate);  continue; }
			if (!minuteSet.Contains(candidate.Minute))   { candidate = candidate.AddMinutes(1); continue; }
			return candidate;
		}
		return null;
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
				AddRange(set, min, max, 1, field, min, max);
			else if (part.StartsWith("*/"))
				AddRange(set, min, max, ParseNumber(part[2..], field), field, min, max);
			else if (part.Contains('-'))
			{
				var dash = part.IndexOf('-');
				var lo   = ParseNumber(part[..dash], field);
				var rest = part[(dash + 1)..];
				var step = 1;
				int hi;
				if (rest.Contains('/'))
				{
					var slash = rest.IndexOf('/');
					hi   = ParseNumber(rest[..slash], field);
					step = ParseNumber(rest[(slash + 1)..], field);
				}
				else
					hi = ParseNumber(rest, field);
				AddRange(set, lo, hi, step, field, min, max);
			}
			else
			{
				var value = ParseNumber(part, field);
				AddRange(set, value, value, 1, field, min, max);
			}
		}
		return set;
	}

	private static int ParseNumber(string text, string field) =>
		int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
			? n
			: throw new ArgumentException($"Invalid cron field '{field}': '{text}' is not a number");

	/// <summary>Adds lo..hi by step. A step of 0 would never advance, so it (and any out-of-range bound) is invalid.</summary>
	private static void AddRange(HashSet<int> set, int lo, int hi, int step, string field, int min, int max)
	{
		if (step <= 0)
			throw new ArgumentException($"Invalid cron field '{field}': step must be at least 1");
		if (lo < min || hi > max || lo > hi)
			throw new ArgumentException($"Invalid cron field '{field}': range {lo}-{hi} is outside {min}-{max}");
		for (long i = lo; i <= hi; i += step) set.Add((int)i);
	}
}
