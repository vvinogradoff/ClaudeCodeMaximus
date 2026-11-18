using System.Collections.Generic;

namespace TessynDesktop.Services;

/// <summary>
/// Plays the Focus item sound (FR.22): the audible cue for a new Focus item. The per-session switch and the
/// global throttle are applied before it by <see cref="FocusItemSoundNotifier"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionSoundService
{
	/// <summary>
	/// Plays <see cref="Models.AppSettingsModel.SessionCompletionSoundFile"/> for a new Focus item of the given
	/// session (FR.22.1). Where it plays — the client or the host — is the implementation's business (FR.22.6).
	/// Swallows and logs any playback failure — never throws.
	/// </summary>
	void PlayFocusItemSound(string nodeId);

	/// <summary>Plays <paramref name="filePath"/> immediately, bypassing the per-session throttle (FR.22.5's Test button).</summary>
	void PlayTestSound(string filePath);

	/// <summary>Enumerates the Windows default sound library (<c>%WINDIR%\Media\*.wav</c>) for the Settings sound picker (FR.22.5).</summary>
	IReadOnlyList<string> GetAvailableSounds();
}
