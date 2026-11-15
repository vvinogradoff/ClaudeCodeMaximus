using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISettingsSectionService" />
/// <remarks>
/// Change detection compares each section's serialised values with the last ones seen, on every <see cref="IAppSettingsService.Saved"/> and before every
/// read or update, so values an Avalonia Settings save wrote (or wrote into memory but not yet saved) are seen. Lock order is the settings lock, then
/// the state lock; notifications are published after the state lock is released. Created by Claude.
/// </remarks>
public sealed class SettingsSectionService : ISettingsSectionService, IDisposable
{
	private sealed class SectionState(ISettingsSection section, string snapshot)
	{
		public ISettingsSection Section { get; } = section;
		public string Snapshot { get; set; } = snapshot;
		public long Version { get; set; } = Constants.SettingsApi.InitialVersion;
	}

	private readonly IAppSettingsService _settings;
	private readonly IRemoteEventBus? _bus;
	private readonly object _gate = new();
	private readonly Dictionary<string, SectionState> _states = new(StringComparer.Ordinal);

	public SettingsSectionService(IAppSettingsService settings, IEnumerable<ISettingsSection> sections, IRemoteEventBus? bus = null)
	{
		_settings = settings;
		_bus      = bus;

		lock (_settings.SyncRoot)
		{
			foreach (var section in sections)
				_states[section.Name] = new SectionState(section, Serialize(section.Read(_settings.Settings)));
		}

		_settings.Saved += OnSaved;
	}

	public void Dispose() => _settings.Saved -= OnSaved;

	/// <inheritdoc />
	public IReadOnlyList<SettingsSectionInfoDto> GetSections()
	{
		lock (_settings.SyncRoot)
		{
			Detect();
			lock (_gate)
				return [.. _states.Values.Select(s => new SettingsSectionInfoDto(s.Section.Name, s.Version))];
		}
	}

	/// <inheritdoc />
	public SettingsGetResult<object> Get(string section)
	{
		lock (_settings.SyncRoot)
		{
			Detect();
			var state = Find(section);
			lock (_gate)
				return new SettingsGetResult<object>(state.Section.Name, state.Version, state.Section.Read(_settings.Settings));
		}
	}

	/// <inheritdoc />
	public long Update(string section, JsonNode patch, long baseVersion)
	{
		lock (_settings.SyncRoot)
		{
			Detect();
			var state = Find(section);
			lock (_gate)
			{
				if (state.Version != baseVersion)
					throw new TdcpException(Constants.SettingsApi.StaleVersionMessage);
			}

			state.Section.Apply(_settings.Settings, patch);
			if (Serialize(state.Section.Read(_settings.Settings)) == state.Snapshot)
				return state.Version;

			_settings.Save();
			Detect(); // the Saved event does this too; a service without the event still needs it

			lock (_gate)
				return state.Version;
		}
	}

	private SectionState Find(string section) =>
		_states.TryGetValue(section, out var state) ? state : throw new TdcpInvalidParamsException($"Unknown settings section '{section}'");

	private void OnSaved()
	{
		try
		{
			lock (_settings.SyncRoot)
				Detect();
		}
		catch (Exception)
		{
			// Never fail the save that raised the event; the next read detects the change anyway.
		}
	}

	/// <summary>Bumps the version of every section whose values changed since the last look and publishes <c>settings.changed</c> for it.</summary>
	private void Detect()
	{
		List<SettingsChangedPayload>? changed = null;
		lock (_settings.SyncRoot)
		{
			lock (_gate)
			{
				foreach (var state in _states.Values)
				{
					var snapshot = Serialize(state.Section.Read(_settings.Settings));
					if (snapshot == state.Snapshot)
						continue;

					state.Snapshot = snapshot;
					state.Version++;
					(changed ??= []).Add(new SettingsChangedPayload(state.Section.Name, state.Version));
				}
			}
		}

		if (changed == null || _bus == null)
			return;

		foreach (var payload in changed)
			_bus.Publish(RemoteProtocol.SettingsApi.ChangedEvent, payload);
	}

	private static string Serialize(object values) => JsonSerializer.Serialize(values, values.GetType(), TdcpJsonOptions.Default);
}
