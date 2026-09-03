using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Default <see cref="ISessionSoundService"/> implementation (FR.22). Plays .wav files via the
/// Win32 <c>winmm.dll</c> <c>PlaySound</c> API rather than <see cref="System.Media.SoundPlayer"/>
/// or a NuGet audio package, so no project reference change is needed. The per-session throttle
/// (FR.22.3) is tracked in memory for the lifetime of this app-wide singleton; the clock and the
/// actual playback call are both injectable so the throttle logic can be unit tested without
/// touching real audio hardware or wall-clock time.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionSoundService : ISessionSoundService
{
	private static readonly ILogger _log = Log.ForContext<SessionSoundService>();

	private const uint SND_ASYNC = 0x0001;
	private const uint SND_FILENAME = 0x00020000;

	[DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);

	private readonly IAppSettingsService _appSettings;
	private readonly Func<DateTimeOffset> _now;
	private readonly Action<string> _play;
	private readonly ConcurrentDictionary<string, DateTimeOffset> _lastPlayedByNodeId = new();

	public SessionSoundService(IAppSettingsService appSettings)
		: this(appSettings, now: null, play: null)
	{
	}

	/// <summary>Test seam: <paramref name="now"/> and <paramref name="play"/> default to real time/playback when null.</summary>
	public SessionSoundService(IAppSettingsService appSettings, Func<DateTimeOffset>? now, Action<string>? play)
	{
		_appSettings = appSettings;
		_now = now ?? (() => DateTimeOffset.UtcNow);
		_play = play ?? PlayWavFile;
	}

	public void PlayCompletionSound(string nodeId)
	{
		if (string.IsNullOrEmpty(nodeId))
			return;

		var throttle = TimeSpan.FromSeconds(Math.Max(0, _appSettings.Settings.SessionCompletionSoundThrottleSeconds));
		var now = _now();

		if (_lastPlayedByNodeId.TryGetValue(nodeId, out var last) && now - last < throttle)
			return;

		_lastPlayedByNodeId[nodeId] = now;
		PlaySafe(_appSettings.Settings.SessionCompletionSoundFile);
	}

	public void PlayTestSound(string filePath) => PlaySafe(filePath);

	public IReadOnlyList<string> GetAvailableSounds()
	{
		try
		{
			var mediaDir = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.Windows),
				Constants.SessionSound.SoundLibrarySubfolder);

			if (!Directory.Exists(mediaDir))
				return Array.Empty<string>();

			return Directory.GetFiles(mediaDir, Constants.SessionSound.SoundFileSearchPattern)
				.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Could not enumerate Windows sound library");
			return Array.Empty<string>();
		}
	}

	private void PlaySafe(string filePath)
	{
		try
		{
			_play(filePath);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Could not play completion sound {File}", filePath);
		}
	}

	private static void PlayWavFile(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
		{
			_log.Warning("Completion sound file not found: {File}", filePath);
			return;
		}

		PlaySound(filePath, IntPtr.Zero, SND_ASYNC | SND_FILENAME);
	}
}
