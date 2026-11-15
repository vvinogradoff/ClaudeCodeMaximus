namespace TessynDesktop.Models.Remote;

/// <summary>Values of the <c>notifications</c> settings section.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record NotificationsSettingsDto(bool NotificationsEnabled, string SessionCompletionSoundFile, int SessionCompletionSoundThrottleSeconds);
