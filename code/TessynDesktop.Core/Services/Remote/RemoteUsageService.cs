using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IRemoteUsageService" />
/// <remarks>Created by Claude</remarks>
public sealed class RemoteUsageService : IRemoteUsageService
{
	private static readonly ILogger _log = Log.ForContext<RemoteUsageService>();

	private readonly IClaudeUsageFetcher _fetcher;
	private readonly IClaudeUsageService _desktopUsage;
	private readonly IClaudeProfileService _profileService;
	private readonly IAppSettingsService _appSettings;
	private readonly IProfileLabelService _labels;
	private readonly IRemoteEventBus _bus;
	private readonly TimeProvider _time;
	private readonly TimeSpan _fetchWait;
	private readonly ConcurrentDictionary<string, ProfileUsageEntry> _entries = new();
	private readonly IEngineUsageSource[] _engineSources;

	public RemoteUsageService(
		IClaudeUsageFetcher fetcher,
		IClaudeUsageService desktopUsage,
		IClaudeProfileService profileService,
		IAppSettingsService appSettings,
		IProfileLabelService labels,
		IRemoteEventBus bus,
		TimeProvider? time = null,
		TimeSpan? fetchWait = null,
		IEnumerable<IEngineUsageSource>? engineSources = null)
	{
		// FR.33.10: a ChatGPT profile's plan windows come from the Codex engine's usage source.
		_engineSources  = engineSources?.ToArray() ?? [];
		foreach (var source in _engineSources)
			source.Changed += PublishChanged;

		_fetcher        = fetcher;
		_desktopUsage   = desktopUsage;
		_profileService = profileService;
		_appSettings    = appSettings;
		_labels         = labels;
		_bus            = bus;
		_time           = time ?? TimeProvider.System;
		_fetchWait      = fetchWait ?? Constants.Remote.Usage.FetchWait;

		_desktopUsage.UsageUpdated += OnDesktopUsageUpdated;
	}

	/// <inheritdoc />
	public async Task<UsageResult> GetAsync(string profileId, CancellationToken cancellationToken)
	{
		if (_engineSources.FirstOrDefault(s => s.Handles(profileId)) is { } engineSource)
			return await engineSource.GetAsync(profileId, cancellationToken);

		var profile = ResolveProfile(profileId);
		var entry   = _entries.GetOrAdd(profile.Id, _ => new ProfileUsageEntry());

		Task? fetch = null;
		lock (entry)
		{
			if (_time.GetUtcNow() >= entry.NextFetchAt)
			{
				if (entry.InFlight == null || entry.InFlight.IsCompleted)
					entry.InFlight = FetchAsync(profile, entry);

				fetch = entry.InFlight;
			}
		}

		if (fetch != null)
			await Task.WhenAny(fetch, Task.Delay(_fetchWait, cancellationToken));

		var name = profile.Name ?? _labels.GetDefaultLabelCached();
		return Build(profile.Id, name, entry);
	}

	private UsageResult Build(string profileId, string profileName, ProfileUsageEntry entry)
	{
		var now = _time.GetUtcNow();
		lock (entry)
		{
			var data = entry.Data;
			if (data != null)
			{
				var current = ClaudeUsageStalenessService.ZeroStaleWindows(data, now);
				return new UsageResult(
					profileId, profileName, UsageState.Ok,
					new UsageWindowDto(current.FiveHourUtilization, data.FiveHourResetsAt, ClaudeUsageStalenessService.IsFiveHourStale(data, now),
						Constants.Remote.Usage.FiveHourLabel, Constants.Remote.Usage.FiveHourMinutes),
					new UsageWindowDto(current.SevenDayUtilization, data.SevenDayResetsAt, ClaudeUsageStalenessService.IsSevenDayStale(data, now),
						Constants.Remote.Usage.SevenDayLabel, Constants.Remote.Usage.SevenDayMinutes),
					entry.FetchedAt);
			}

			var inFlight = entry.InFlight is { IsCompleted: false };
			return new UsageResult(profileId, profileName, inFlight ? UsageState.Pending : entry.LastState, null, null, null);
		}
	}

	private async Task FetchAsync(ResolvedProfile profile, ProfileUsageEntry entry)
	{
		UsageFetchResultModel result;
		try
		{
			using var timeout = new CancellationTokenSource(Constants.Remote.Usage.FetchTimeout);
			result = await _fetcher.FetchAsync(profile.CredentialsPath, timeout.Token);
		}
		catch (Exception ex)
		{
			_log.Debug(ex, "Usage fetch for profile {ProfileId} did not complete", profile.Id);
			result = new UsageFetchResultModel(UsageState.Unavailable, null);
		}

		var now     = _time.GetUtcNow();
		var changed = false;
		lock (entry)
		{
			entry.LastState = result.State;
			if (result.State == UsageState.Ok && result.Data != null)
			{
				changed         = entry.Data != result.Data;
				entry.Data      = result.Data;
				entry.FetchedAt = now;
			}

			entry.NextFetchAt = now + (result.State == UsageState.Unavailable
				? Constants.Remote.Usage.FailureRetry
				: Constants.Remote.Usage.CacheDuration);
		}

		if (changed)
			PublishChanged(profile.Id);
	}

	private void PublishChanged(string profileId) =>
		_bus.PublishCoalesced(Constants.Remote.Events.UsageChanged, profileId,
			() => new UsageChangedPayload(profileId), Constants.Remote.Usage.ChangedWindow);

	/// <summary>
	/// The desktop poller (or a profile switch) updated its reading. Runs on the UI thread, so it only schedules work: the
	/// coalesced factory finds which profile is active, drops that profile's cache entry's freshness (the next usage.get
	/// asks the API again) and announces the change.
	/// </summary>
	private void OnDesktopUsageUpdated(object? sender, EventArgs e) =>
		_bus.PublishCoalesced(Constants.Remote.Events.UsageChanged, "desktop", HandleDesktopUpdate, Constants.Remote.Usage.ChangedWindow);

	private object? HandleDesktopUpdate()
	{
		var activePath = _desktopUsage.ActiveCredentialsPath;
		if (activePath == null)
			return null;

		var match = EnumerateProfiles().FirstOrDefault(p => string.Equals(p.CredentialsPath, activePath, StringComparison.OrdinalIgnoreCase));
		if (match == null)
			return null;

		if (_entries.TryGetValue(match.Id, out var entry))
		{
			lock (entry)
				entry.NextFetchAt = DateTimeOffset.MinValue;
		}

		return new UsageChangedPayload(match.Id);
	}

	private ResolvedProfile ResolveProfile(string profileId)
	{
		var match = EnumerateProfiles().FirstOrDefault(p => p.Id == profileId);
		return match ?? throw new TdcpInvalidParamsException($"Unknown profile '{profileId}'");
	}

	private ResolvedProfile[] EnumerateProfiles()
	{
		ClaudeProfileModel[] stored;
		lock (_appSettings.SyncRoot)
			stored = [.. _appSettings.Settings.Profiles];

		var result = new ResolvedProfile[stored.Length + 1];
		result[0] = new ResolvedProfile(Constants.Remote.DefaultOptionId, null, ClaudeCredentialsPaths.Resolve(null));
		for (var i = 0; i < stored.Length; i++)
		{
			var configDir = _profileService.GetConfigDirForProfile(i + 1, stored);
			result[i + 1] = new ResolvedProfile(stored[i].ProfileId, stored[i].DisplayName, ClaudeCredentialsPaths.Resolve(configDir));
		}

		return result;
	}

	/// <summary>A profile as usage sees it; <see cref="Name"/> is null for Default, whose label is resolved lazily.</summary>
	private sealed record ResolvedProfile(string Id, string? Name, string CredentialsPath);

	private sealed class ProfileUsageEntry
	{
		public ClaudeUsageData? Data { get; set; }
		public DateTimeOffset? FetchedAt { get; set; }
		public UsageState LastState { get; set; } = UsageState.Unavailable;
		public DateTimeOffset NextFetchAt { get; set; } = DateTimeOffset.MinValue;
		public Task? InFlight { get; set; }
	}
}
