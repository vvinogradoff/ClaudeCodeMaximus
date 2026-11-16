using System.Text.Json;
using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// An in-memory <see cref="ISettingsSectionService"/> for the Settings E2E: the real <c>SettingsRpcModule</c> serves it over the loopback host, so the app talks the real wire protocol.
/// Versions and the stale-version refusal behave as the daemon's: an update with an old <c>baseVersion</c> is refused with "Settings changed". The test can play "another client" with <see cref="ChangeElsewhere"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ScriptedSettingsSections : ISettingsSectionService
{
	private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
	private readonly object _gate = new();
	private readonly Dictionary<string, long> _versions = new()
	{
		["general"] = 1,
		["notifications"] = 1,
		["telegram"] = 1,
	};

	private AvaloniaUiSettingsDto? _avaloniaUi;

	private GeneralSettingsDto _general = new(@"C:\Sessions", "claude", @"C:\Src", string.Empty, "http://localhost:11434", false,
		new FileAccessSettingsDto(FileAccessModeDto.ReferencedOnly, 65536, 1000, 2000, 3000, 4000), []);

	private NotificationsSettingsDto _notifications = new(true, string.Empty, 180);
	private TelegramSettingsDto _telegram = new(false, 0, [], string.Empty, TelegramMirrorModeDto.Full);

	public NotificationsSettingsDto Notifications
	{
		get
		{
			lock (_gate)
				return _notifications;
		}
	}

	public int UpdateCount { get; private set; }

	/// <summary>Makes the daemon serve the read-only <c>avalonia-ui</c> section the first-start import reads (W10a E2E).</summary>
	public void ServeAvaloniaUi(AvaloniaUiSettingsDto values)
	{
		lock (_gate)
		{
			_avaloniaUi = values;
			_versions["avalonia-ui"] = 1;
		}
	}

	public IReadOnlyList<SettingsSectionInfoDto> GetSections()
	{
		lock (_gate)
			return [.. _versions.Select(v => new SettingsSectionInfoDto(v.Key, v.Value))];
	}

	public SettingsGetResult<object> Get(string section)
	{
		lock (_gate)
		{
			object values = section switch
			{
				"general" => _general,
				"notifications" => _notifications,
				"telegram" => _telegram,
				"avalonia-ui" when _avaloniaUi != null => _avaloniaUi,
				_ => throw new TdcpInvalidParamsException($"Unknown settings section '{section}'"),
			};
			return new SettingsGetResult<object>(section, _versions[section], values);
		}
	}

	public long Update(string section, JsonNode patch, long baseVersion)
	{
		lock (_gate)
		{
			if (!_versions.TryGetValue(section, out var current))
				throw new TdcpInvalidParamsException($"Unknown settings section '{section}'");

			if (baseVersion != current)
				throw new TdcpException("Settings changed");

			UpdateCount++;
			switch (section)
			{
				case "notifications":
					var patchValue = patch.Deserialize<NotificationsSettingsPatch>(Web) ?? new NotificationsSettingsPatch();
					_notifications = _notifications with
					{
						NotificationsEnabled = patchValue.NotificationsEnabled ?? _notifications.NotificationsEnabled,
						SessionCompletionSoundFile = patchValue.SessionCompletionSoundFile ?? _notifications.SessionCompletionSoundFile,
						SessionCompletionSoundThrottleSeconds = patchValue.SessionCompletionSoundThrottleSeconds ?? _notifications.SessionCompletionSoundThrottleSeconds,
					};
					break;
				case "general":
					var general = patch.Deserialize<GeneralSettingsPatch>(Web) ?? new GeneralSettingsPatch();
					_general = _general with { HttpsProxy = general.HttpsProxy ?? _general.HttpsProxy, MediaPreviewHosts = general.MediaPreviewHosts ?? _general.MediaPreviewHosts };
					break;
			}

			return _versions[section] = current + 1;
		}
	}

	/// <summary>Another client changes the sound file and so moves the section's version.</summary>
	public void ChangeElsewhere(string soundFile)
	{
		lock (_gate)
		{
			_notifications = _notifications with { SessionCompletionSoundFile = soundFile };
			_versions["notifications"]++;
		}
	}
}
