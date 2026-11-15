using System;
using System.Collections.Generic;
using System.Threading;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>The desktop host's toast and sound, recorded instead of shown.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class RecordingHostNotifications : INotificationService, ISessionSoundService
{
	private int _toasts;
	private int _sounds;

	public List<(string NodeId, string Title, string Message)> Toasts { get; } = [];
	public int ToastCount => Volatile.Read(ref _toasts);
	public int SoundCount => Volatile.Read(ref _sounds);
	public List<string> TestSounds { get; } = [];

	public bool ShowUserNotification(string nodeId, string title, string message)
	{
		lock (Toasts)
			Toasts.Add((nodeId, title, message));
		Interlocked.Increment(ref _toasts);
		return true;
	}

	public void RegisterActivationHandler(Action<string> onNodeIdActivated)
	{
	}

	public void PlayCompletionSound(string nodeId) => Interlocked.Increment(ref _sounds);

	public void PlayTestSound(string filePath) => TestSounds.Add(filePath);

	public IReadOnlyList<string> GetAvailableSounds() => [];
}
