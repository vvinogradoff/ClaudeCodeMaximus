using System.Collections.Generic;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>An <see cref="ISessionSoundService"/> that plays nothing: it records the test plays and serves a fixed sound list.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class RecordingSoundService : ISessionSoundService
{
	public List<string> TestPlays { get; } = [];

	public List<string> Available { get; set; } = [@"C:\Windows\Media\Alarm01.wav", @"C:\Windows\Media\chimes.wav"];

	public void PlayFocusItemSound(string nodeId)
	{
	}

	public void PlayTestSound(string filePath) => TestPlays.Add(filePath);

	public IReadOnlyList<string> GetAvailableSounds() => Available;
}
