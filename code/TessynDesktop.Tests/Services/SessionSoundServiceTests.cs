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

	public void Load() { }
	public void Save() { }
}

/// <remarks>Created by Claude</remarks>
public sealed class SessionSoundServiceTests
{
	private readonly FakeAppSettingsServiceForSound _appSettings = new();
	private readonly List<string> _played = new();
	private DateTimeOffset _now = DateTimeOffset.UtcNow;

	private SessionSoundService MakeService(int throttleSeconds = 180)
	{
		_appSettings.Settings.SessionCompletionSoundThrottleSeconds = throttleSeconds;
		_appSettings.Settings.SessionCompletionSoundFile = "fake.wav";
		return new SessionSoundService(_appSettings, () => _now, file => _played.Add(file));
	}

	[Fact]
	public void PlayCompletionSound_FirstCallForSession_Plays()
	{
		var svc = MakeService();

		svc.PlayCompletionSound("node-1");

		Assert.Single(_played);
		Assert.Equal("fake.wav", _played[0]);
	}

	[Fact]
	public void PlayCompletionSound_SecondCallWithinThrottleWindow_IsSuppressed()
	{
		var svc = MakeService(throttleSeconds: 180);

		svc.PlayCompletionSound("node-1");
		_now = _now.AddSeconds(60);
		svc.PlayCompletionSound("node-1");

		Assert.Single(_played);
	}

	[Fact]
	public void PlayCompletionSound_CallAfterThrottleWindowElapses_PlaysAgain()
	{
		var svc = MakeService(throttleSeconds: 180);

		svc.PlayCompletionSound("node-1");
		_now = _now.AddSeconds(181);
		svc.PlayCompletionSound("node-1");

		Assert.Equal(2, _played.Count);
	}

	[Fact]
	public void PlayCompletionSound_DifferentSessions_ThrottleIndependently()
	{
		var svc = MakeService(throttleSeconds: 180);

		svc.PlayCompletionSound("node-1");
		svc.PlayCompletionSound("node-2");

		Assert.Equal(2, _played.Count);
	}

	[Fact]
	public void PlayCompletionSound_EmptyNodeId_DoesNothing()
	{
		var svc = MakeService();

		svc.PlayCompletionSound(string.Empty);

		Assert.Empty(_played);
	}

	[Fact]
	public void PlayTestSound_BypassesThrottle()
	{
		var svc = MakeService(throttleSeconds: 180);

		svc.PlayCompletionSound("node-1");
		svc.PlayTestSound("other.wav");
		svc.PlayTestSound("other.wav");

		Assert.Equal(3, _played.Count);
	}

	[Fact]
	public void PlayCompletionSound_PlaybackThrows_IsSwallowed()
	{
		var svc = new SessionSoundService(_appSettings, () => _now, _ => throw new InvalidOperationException("no audio device"));

		var ex = Record.Exception(() => svc.PlayCompletionSound("node-1"));

		Assert.Null(ex);
	}
}
