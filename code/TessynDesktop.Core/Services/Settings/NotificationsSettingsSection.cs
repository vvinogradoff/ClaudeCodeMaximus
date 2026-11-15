using System;
using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>The <c>notifications</c> section: the toast toggle and the session completion sound. The whitelist is the member list of <see cref="NotificationsSettingsPatch"/>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class NotificationsSettingsSection : ISettingsSection
{
	/// <inheritdoc />
	public string Name => RemoteProtocol.SettingsApi.NotificationsSection;

	/// <inheritdoc />
	public object Read(AppSettingsModel settings) =>
		new NotificationsSettingsDto(settings.NotificationsEnabled, settings.SessionCompletionSoundFile, settings.SessionCompletionSoundThrottleSeconds);

	/// <inheritdoc />
	public Action CaptureRestore(AppSettingsModel settings)
	{
		var (enabled, file, throttle) = (settings.NotificationsEnabled, settings.SessionCompletionSoundFile, settings.SessionCompletionSoundThrottleSeconds);
		return () => (settings.NotificationsEnabled, settings.SessionCompletionSoundFile, settings.SessionCompletionSoundThrottleSeconds) = (enabled, file, throttle);
	}

	/// <inheritdoc />
	public void Apply(AppSettingsModel settings, JsonNode patch)
	{
		var p = SettingsPatchReader.Read<NotificationsSettingsPatch>(patch);

		var soundFile = p.SessionCompletionSoundFile == null ? null : SettingsPatchReader.WavFile(nameof(p.SessionCompletionSoundFile), p.SessionCompletionSoundFile);
		var throttle  = p.SessionCompletionSoundThrottleSeconds is { } seconds
			? SettingsPatchReader.InRange(nameof(p.SessionCompletionSoundThrottleSeconds), seconds, 0, Constants.SettingsApi.MaxSoundThrottleSeconds)
			: (int?)null;

		if (p.NotificationsEnabled is { } enabled) settings.NotificationsEnabled = enabled;
		if (soundFile != null)                     settings.SessionCompletionSoundFile = soundFile;
		if (throttle is { } value)                 settings.SessionCompletionSoundThrottleSeconds = value;
	}
}
