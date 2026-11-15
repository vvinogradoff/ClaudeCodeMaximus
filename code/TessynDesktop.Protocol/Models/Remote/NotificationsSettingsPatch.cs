namespace TessynDesktop.Models.Remote;

/// <summary>Patch of the <c>notifications</c> section (see <see cref="GeneralSettingsPatch"/> for the rules).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record NotificationsSettingsPatch(bool? NotificationsEnabled = null, string? SessionCompletionSoundFile = null, int? SessionCompletionSoundThrottleSeconds = null);
