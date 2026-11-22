using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Codex.Protocol;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.Codex;

/// <summary>
/// The Codex plan windows of each ChatGPT profile (FR.33.10), the Codex engine's usage source.
/// <list type="bullet">
/// <item>Read with <c>account/rateLimits/read</c> (the <c>codex</c> bucket of <c>rateLimitsByLimitId</c>, else <c>rateLimits</c>), at most once per
/// cache duration and again after a failure's retry time (FR.18.6); a read error keeps the last reading.</item>
/// <item>Kept current with <c>account/rateLimits/updated</c>, a sparse update merged into the last reading: a null window or field does not clear a
/// known value.</item>
/// <item>A window whose reset time has passed is shown as 0 and stale (FR.18.8).</item>
/// <item>On the wire (phase 5 adds the labels) the top window goes in the existing 5-hour slot and the bottom one in the 7-day slot.</item>
/// </list>
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class CodexUsageService : IEngineUsageSource
{
	private static readonly ILogger _log = Log.ForContext<CodexUsageService>();

	private readonly CodexHostPool _pool;
	private readonly ICodexProfileService _profiles;
	private readonly TimeProvider _time;
	private readonly CodexProfileNaming _naming = new();
	private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<CodexAppServerHost, byte> _watched = new();

	public CodexUsageService(CodexHostPool pool, ICodexProfileService profiles, TimeProvider? time = null)
	{
		_pool     = pool;
		_profiles = profiles;
		_time     = time ?? TimeProvider.System;

		_pool.HostCreated += Watch;
		foreach (var host in _pool.Hosts)
			Watch(host);
	}

	/// <inheritdoc />
	public event Action<string>? Changed;

	/// <inheritdoc />
	public bool Handles(string profileId) => _naming.IsCodexProfileId(profileId);

	/// <summary>The last reading of the profile, with staleness applied; null before the first.</summary>
	public CodexUsageReadingModel? GetReading(string profileId)
	{
		if (!_entries.TryGetValue(profileId, out var entry))
			return null;

		lock (entry)
			return entry.Snapshot is null ? null : Reading(profileId, entry.Snapshot, entry.FetchedAt!.Value);
	}

	/// <summary>The profile's reading, read again when it is older than the cache duration; null when there is none.</summary>
	public async Task<CodexUsageReadingModel?> ReadAsync(string profileId, CancellationToken cancellationToken = default)
	{
		var entry = _entries.GetOrAdd(profileId, _ => new Entry());
		Task? fetch = null;
		lock (entry)
		{
			if (_time.GetUtcNow() >= entry.NextFetchAt)
				fetch = entry.InFlight is { IsCompleted: false } running ? running : entry.InFlight = FetchAsync(profileId, entry);
		}

		if (fetch is not null)
			await fetch.WaitAsync(cancellationToken).ConfigureAwait(false);

		return GetReading(profileId);
	}

	/// <inheritdoc />
	public async Task<UsageResult> GetAsync(string profileId, CancellationToken cancellationToken)
	{
		var name    = _profiles.DisplayNameOf(profileId) ?? throw new TdcpInvalidParamsException($"Unknown profile '{profileId}'");
		var reading = await ReadAsync(profileId, cancellationToken).ConfigureAwait(false);
		if (reading is null)
		{
			var state = _entries.TryGetValue(profileId, out var entry) ? entry.LastState : UsageState.Unavailable;
			return new UsageResult(profileId, name, state, null, null, null);
		}

		return new UsageResult(profileId, name, UsageState.Ok, Window(reading.Primary), Window(reading.Secondary), reading.FetchedAtUtc);
	}

	/// <summary>
	/// The FR.33.13 suffix of a limits error: <c> (resets &lt;time&gt;)</c> for the exhausted window of the last reading (the fullest one when Codex
	/// does not say which), or empty when nothing is known.
	/// </summary>
	public string LimitResetSuffix(string profileId)
	{
		var reading = GetReading(profileId);
		var window  = new[] { reading?.Primary, reading?.Secondary }.Where(w => w?.ResetsAtUtc is not null)
			.OrderByDescending(w => w!.IsExhausted).ThenByDescending(w => w!.UsedPercent).FirstOrDefault();
		return window?.ResetsAtUtc is { } resets
			? string.Format(Constants.Codex.Usage.ResetsFormat, resets.ToLocalTime().ToString(Constants.Codex.Usage.ResetTimeFormat, CultureInfo.InvariantCulture))
			: string.Empty;
	}

	/// <summary>The label of a window from its own duration: <c>5h</c>, <c>7d</c>, or <c>&lt;n&gt;m</c>; never assumed.</summary>
	public string Label(int? windowDurationMins)
	{
		if (windowDurationMins is not { } minutes || minutes <= 0)
			return string.Empty;
		if (minutes % Constants.Codex.Usage.MinutesPerDay == 0)
			return string.Format(CultureInfo.InvariantCulture, Constants.Codex.Usage.DaysLabelFormat, minutes / Constants.Codex.Usage.MinutesPerDay);
		if (minutes % Constants.Codex.Usage.MinutesPerHour == 0)
			return string.Format(CultureInfo.InvariantCulture, Constants.Codex.Usage.HoursLabelFormat, minutes / Constants.Codex.Usage.MinutesPerHour);
		return string.Format(CultureInfo.InvariantCulture, Constants.Codex.Usage.MinutesLabelFormat, minutes);
	}

	private async Task FetchAsync(string profileId, Entry entry)
	{
		CodexRateLimitSnapshot? snapshot = null;
		var state = UsageState.Unavailable;
		try
		{
			if (_profiles.Find(profileId) is { } profile)
			{
				using var timeout = new CancellationTokenSource(Constants.Remote.Usage.FetchTimeout);
				var result = await _pool.GetHost(profile).ReadRateLimitsAsync(timeout.Token).ConfigureAwait(false);
				snapshot = result.RateLimitsByLimitId is { } byId && byId.TryGetValue(Constants.Codex.Usage.CodexLimitId, out var bucket) ? bucket : result.RateLimits;
				state = UsageState.Ok;
			}
		}
		catch (Exception ex)
		{
			_log.Debug(ex, "Codex usage read for profile {Profile} failed; the last reading is kept", profileId);
		}

		var now = _time.GetUtcNow();
		var changed = false;
		lock (entry)
		{
			entry.LastState = state;
			if (snapshot is not null)
			{
				// A read is a full reading: it replaces the last one, so a window it no longer has (a plan change) is hidden (FR.33.10).
				entry.Snapshot  = snapshot;
				entry.FetchedAt = now;
				changed         = true;
			}

			entry.NextFetchAt = now + (state == UsageState.Ok ? Constants.Remote.Usage.CacheDuration : Constants.Remote.Usage.FailureRetry);
		}

		if (changed)
			Changed?.Invoke(profileId);
	}

	private void Watch(CodexAppServerHost host)
	{
		if (_watched.TryAdd(host, 0))
			host.NotificationReceived += notification => OnNotification(host, notification);
	}

	private void OnNotification(CodexAppServerHost host, CodexNotification notification)
	{
		if (notification.Method != Constants.Codex.Methods.AccountRateLimitsUpdated)
			return;

		CodexRateLimitSnapshot? update;
		try
		{
			update = notification.Params.Deserialize<CodexRateLimitsUpdatedNotification>(Constants.Codex.SerializerOptions)?.RateLimits;
		}
		catch (JsonException ex)
		{
			_log.Warning(ex, "account/rateLimits/updated of profile {Profile} could not be read", host.ProfileId);
			return;
		}

		if (update is null || (update.LimitId is not null && !string.Equals(update.LimitId, Constants.Codex.Usage.CodexLimitId, StringComparison.OrdinalIgnoreCase)))
			return;

		var entry = _entries.GetOrAdd(host.ProfileId, _ => new Entry());
		lock (entry)
		{
			entry.Snapshot  = Merge(entry.Snapshot, update);
			entry.FetchedAt = _time.GetUtcNow();
			entry.LastState = UsageState.Ok;
		}

		Changed?.Invoke(host.ProfileId);
	}

	/// <summary>
	/// A sparse <c>account/rateLimits/updated</c> over the last reading: a null window or field keeps the known value. The out-of-credits type
	/// follows the same rule, except that an update that carries a window is a reading of the plan's state, so its null clears a known type
	/// (otherwise an exhausted plan would look exhausted for ever).
	/// </summary>
	private CodexRateLimitSnapshot Merge(CodexRateLimitSnapshot? known, CodexRateLimitSnapshot update) =>
		known is null
			? update
			: new CodexRateLimitSnapshot
			{
				LimitId              = update.LimitId ?? known.LimitId,
				LimitName            = update.LimitName ?? known.LimitName,
				Primary              = MergeWindow(known.Primary, update.Primary),
				Secondary            = MergeWindow(known.Secondary, update.Secondary),
				Credits              = update.Credits ?? known.Credits,
				PlanType             = update.PlanType ?? known.PlanType,
				RateLimitReachedType = update.RateLimitReachedType
				                       ?? (update.Primary is not null || update.Secondary is not null ? null : known.RateLimitReachedType),
				SpendControlReached  = update.SpendControlReached ?? known.SpendControlReached,
			};

	private CodexRateLimitWindow? MergeWindow(CodexRateLimitWindow? known, CodexRateLimitWindow? update) =>
		update is null
			? known
			: new CodexRateLimitWindow
			{
				UsedPercent        = update.UsedPercent,
				WindowDurationMins = update.WindowDurationMins ?? known?.WindowDurationMins,
				ResetsAt           = update.ResetsAt ?? known?.ResetsAt,
			};

	private CodexUsageReadingModel Reading(string profileId, CodexRateLimitSnapshot snapshot, DateTimeOffset fetchedAt) =>
		new(profileId, Window(snapshot.Primary, snapshot), Window(snapshot.Secondary, snapshot), snapshot.PlanType, snapshot.RateLimitReachedType, fetchedAt);

	private CodexUsageWindowModel? Window(CodexRateLimitWindow? window, CodexRateLimitSnapshot snapshot)
	{
		if (window is null)
			return null;

		DateTimeOffset? resets = window.ResetsAt is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
		var stale     = resets is { } at && at <= _time.GetUtcNow();
		var exhausted = !string.IsNullOrEmpty(snapshot.RateLimitReachedType) && window.UsedPercent >= 100;
		return new CodexUsageWindowModel(Label(window.WindowDurationMins), stale ? 0 : window.UsedPercent, resets, window.WindowDurationMins, stale, exhausted);
	}

	private UsageWindowDto? Window(CodexUsageWindowModel? window) =>
		window?.ResetsAtUtc is { } resets ? new UsageWindowDto(window.UsedPercent, resets, window.IsStale) : null;
}
