using System.Collections.Generic;

namespace TessynDesktop.Services;

/// <summary>
/// Plays the Session Completion Sound (FR.22): an audible cue on natural interactive turn
/// completion, throttled per session.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionSoundService
{
	/// <summary>
	/// Plays <see cref="Models.AppSettingsModel.SessionCompletionSoundFile"/> for the given session,
	/// unless a sound already played for that <paramref name="nodeId"/> within
	/// <see cref="Models.AppSettingsModel.SessionCompletionSoundThrottleSeconds"/> (FR.22.3). Swallows
	/// and logs any playback failure — never throws.
	/// </summary>
	void PlayCompletionSound(string nodeId);

	/// <summary>Plays <paramref name="filePath"/> immediately, bypassing the per-session throttle (FR.22.5's Test button).</summary>
	void PlayTestSound(string filePath);

	/// <summary>Enumerates the Windows default sound library (<c>%WINDIR%\Media\*.wav</c>) for the Settings sound picker (FR.22.5).</summary>
	IReadOnlyList<string> GetAvailableSounds();
}
