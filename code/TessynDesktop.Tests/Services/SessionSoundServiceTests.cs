using System;
using System.Collections.Generic;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

// ── Fakes ─────────────────────────────────────────────────────────────────────

internal sealed class FakeAppSettingsServiceForSound : IAppSettingsService
{
	public AppSettingsModel Settings { get; } = new();
	public object SyncRoot { get; } = new();

	public void Load() { }
	public void Save() { }
}

/// <summary>The host's player of the Focus item sound (FR.22.4). The throttle is the notifier's (FocusItemSoundNotifierTests).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionSoundServiceTests
{
	private readonly FakeAppSettingsServiceForSound _appSettings = new();
	private readonly List<string> _played = new();

	private SessionSoundService MakeService()
	{
		_appSettings.Settings.SessionCompletionSoundFile = "fake.wav";
		return new SessionSoundService(_appSettings, file => _played.Add(file));
	}

	[Fact]
	public void PlayFocusItemSound_PlaysTheConfiguredFile()
	{
		var svc = MakeService();

		svc.PlayFocusItemSound("node-1");

		Assert.Equal(["fake.wav"], _played);
	}

	[Fact]
	public void PlayFocusItemSound_EmptyNodeId_DoesNothing()
	{
		var svc = MakeService();

		svc.PlayFocusItemSound(string.Empty);

		Assert.Empty(_played);
	}

	[Fact]
	public void PlayTestSound_PlaysTheGivenFile()
	{
		var svc = MakeService();

		svc.PlayTestSound("other.wav");
		svc.PlayTestSound("other.wav");

		Assert.Equal(["other.wav", "other.wav"], _played);
	}

	[Fact]
	public void PlayFocusItemSound_PlaybackThrows_IsSwallowed()
	{
		var svc = new SessionSoundService(_appSettings, _ => throw new InvalidOperationException("no audio device"));

		var ex = Record.Exception(() => svc.PlayFocusItemSound("node-1"));

		Assert.Null(ex);
	}

	[Fact]
	public void TheDefaultThrottle_IsThreeSeconds()
	{
		Assert.Equal(3, _appSettings.Settings.SessionCompletionSoundThrottleSeconds);
	}
}
