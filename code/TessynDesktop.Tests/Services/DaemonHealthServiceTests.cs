using System;
using System.IO;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Contract;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Package J4: <see cref="DaemonHealthService"/> behind <c>daemon.health</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonHealthServiceTests : IDisposable
{
	private static readonly DateTimeOffset Started = new(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);

	private readonly ImportTestEnv _env = new();
	private readonly ManualTimeProvider _clock = new();
	private readonly FakeSessionIndexActivity _activity = new();
	private readonly RemoteEventBus _bus = new();
	private readonly EventLog _log;
	private readonly DaemonRunInfo _runInfo = new();
	private readonly string _logs;

	public DaemonHealthServiceTests()
	{
		_log  = new EventLog(_bus);
		_logs = Path.Combine(_env.Root, "logs");
		Directory.CreateDirectory(_logs);
	}

	public void Dispose()
	{
		_bus.Dispose();
		_env.Dispose();
	}

	private DaemonHealthService Create(string? logDirectory = null, bool withLog = true, bool withActivity = true) =>
		new(_runInfo, new RemoteTreeReader(_env.Settings), _env.Registry, withLog ? _log : null, withActivity ? _activity : null, _clock, Started, logDirectory ?? _logs);

	[Fact]
	public void TheUptime_IsCountedFromTheStart_AndTheRunIdIsTheDaemons()
	{
		_clock.Advance(TimeSpan.FromHours(4) + TimeSpan.FromSeconds(30));

		var health = Create().Get();

		Assert.Equal(4 * 3600 + 30 + 4 * 3600, health.UptimeSeconds);   // the manual clock starts four hours after Started
		Assert.Equal(_runInfo.RunId, health.RunId);
	}

	[Fact]
	public void TheEventHead_IsTheEventLogs()
	{
		_bus.Publish("session.changed", new { id = 1 });
		_bus.Publish("session.changed", new { id = 2 });

		Assert.Equal(_log.HeadSeq, Create().Get().EventSeq);
		Assert.Equal(2, Create().Get().EventSeq);
		Assert.Equal(0, Create(withLog: false).Get().EventSeq);
	}

	[Fact]
	public void TheRunningSessions_AreCounted_WorkersIncluded()
	{
		var directory = _env.AddDirectory("proj");
		var a = _env.Registry.AddSession(directory, "A");
		var b = _env.Registry.AddSession(directory, "B");
		_env.Registry.AddSession(directory, "idle");
		_env.Registry.SetSessionRunning(a.NodeId, true);
		_env.Registry.SetSessionRunning(b.NodeId, true);

		Assert.Equal(2, Create().Get().SessionsRunning);

		_env.Registry.SetSessionRunning(a.NodeId, false);
		Assert.Equal(1, Create().Get().SessionsRunning);
	}

	[Fact]
	public void TheIndex_IsIdle_OrScanning_WithTheQueueBehindTheRunningScan()
	{
		var idle = Create().Get().Index;
		_activity.ActiveScans = 3;
		_activity.LastScanFinishedUtc = Started.AddHours(1);
		var busy = Create().Get().Index;

		Assert.Equal(new DaemonIndexHealthDto("idle", null, 0), idle);
		Assert.Equal(new DaemonIndexHealthDto("scanning", Started.AddHours(1), 2), busy);
		Assert.Equal(new DaemonIndexHealthDto("idle", null, 0), Create(withActivity: false).Get().Index);
	}

	[Fact]
	public void TheLogSize_AddsUpTheLogFiles_AndIsNullWithoutTheFolder()
	{
		File.WriteAllBytes(Path.Combine(_logs, "tessyn20261104.log"), new byte[1000]);
		File.WriteAllBytes(Path.Combine(_logs, "tessyn20261104_001.log"), new byte[234]);
		File.WriteAllBytes(Path.Combine(_logs, "notes.txt"), new byte[5000]);

		Assert.Equal(1234, Create().Get().LogBytes);
		Assert.Null(Create(Path.Combine(_env.Root, "missing")).Get().LogBytes);
	}

	[Fact]
	public void TheProcessCounters_AreThoseOfThisProcess()
	{
		var health = Create().Get();

		Assert.True(health.WorkingSetMb > 0);
		Assert.True(health.Gen2Count >= 0);
	}

	[Fact]
	public void TheDefaultStart_IsTheStartOfThisProcess_SoTheUptimeIsPositive()
	{
		var health = new DaemonHealthService(_runInfo, new RemoteTreeReader(_env.Settings), _env.Registry, logDirectory: _logs).Get();

		Assert.True(health.UptimeSeconds >= 0);
	}
}
