using System.Diagnostics.CodeAnalysis;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

/// <summary>Reads the package J3 notifications of <see cref="ITessynClient.NotificationReceived"/> as typed payloads; false when it is another method or does not fit.</summary>
public static class SettingsNotificationExtensions
{
	/// <summary><c>settings.changed</c>: a section's values changed (reload it with <c>SettingsGetAsync</c>).</summary>
	public static bool TryGetSettingsChanged(this RemoteNotification notification, [NotNullWhen(true)] out SettingsChangedPayload? payload) =>
		notification.TryRead(RemoteProtocol.SettingsApi.ChangedEvent, out payload);

	/// <summary><c>profiles.changed</c>: the profile list or an auth state changed (reload <c>ProfileListAsync</c>).</summary>
	public static bool IsProfilesChanged(this RemoteNotification notification) =>
		notification.Method == RemoteProtocol.ProfilesAdmin.ChangedEvent;

	/// <summary><c>profiles.codexLoginCompleted</c> (FR.33.5): a ChatGPT sign-in ended; match <c>loginId</c> with <c>ProfileCodexLoginAsync</c>'s answer.</summary>
	public static bool TryGetCodexLoginCompleted(this RemoteNotification notification, [NotNullWhen(true)] out ProfileCodexLoginCompletedPayload? payload) =>
		notification.TryRead(RemoteProtocol.CodexProfilesAdmin.LoginCompletedEvent, out payload);
}
