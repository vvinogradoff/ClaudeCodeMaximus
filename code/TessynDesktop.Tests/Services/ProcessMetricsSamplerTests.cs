using System;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>D1.3 sampler snapshot sanity and D1.6 spike gating.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProcessMetricsSamplerTests : IDisposable
{
	private readonly DiagnosticsTestEnv _env = new();

	public void Dispose() => _env.Dispose();

	[Fact]
	public void GetSnapshot_ReportsPlausibleProcessFigures()
	{
		using var sampler = new ProcessMetricsSampler(_env.Settings, new FakeRuntimeInspector(), _env.Store);

		var s = sampler.GetSnapshot();

		Assert.True(s.PrivateMb > 10);
		Assert.True(s.WorkingSetMb > 10);
		Assert.True(s.GcHeapMb > 0);
		Assert.True(s.GcCommittedMb > 0);
		Assert.True(s.GcFragmentedMb >= 0);
		Assert.True(s.LohMb >= 0);
		Assert.True(s.Handles > 0);
		Assert.True(s.Threads > 0);
		Assert.True(s.ThreadPoolThreads > 0);
		Assert.True(s.ThreadPoolPendingWork >= 0);
		Assert.True(s.Gen0Collections >= s.Gen1Collections && s.Gen1Collections >= s.Gen2Collections);
		Assert.True(s.GcPausePercent >= 0);
		Assert.True(s.GcTotalPauseMs >= 0);
		Assert.True(s.CpuPercent >= 0);
		Assert.Equal(s.PrivateMb - s.GcCommittedMb, s.NativeEstimateMb, precision: 6);
	}

	[Fact]
	public void Start_LogsMemLineEachInterval()
	{
		_env.Settings.MetricsIntervalSeconds = 1;
		using var sampler = new ProcessMetricsSampler(_env.Settings, new FakeRuntimeInspector(), _env.Store, log: _env.Sink.CreateLogger());

		sampler.Start();

		Assert.True(DiagnosticsTestEnv.WaitFor(() => _env.Sink.Messages(Serilog.Events.LogEventLevel.Information).Count > 0, 5000));
		var line = _env.Sink.Messages(Serilog.Events.LogEventLevel.Information)[0];
		Assert.StartsWith("MEM priv=", line);
		Assert.Contains("native~=", line);
		Assert.Contains("handles=", line);
	}

	[Fact]
	public void SpikeGate_BelowThreshold_NeverFires()
	{
		var gate = new MemorySpikeGate(_env.Settings, _env.Clock);

		Assert.False(gate.ShouldCapture(_env.Settings.MemorySpikeThresholdMb - 1));
	}

	[Fact]
	public void SpikeGate_AtThreshold_FiresOnceThenStaysQuietWhileAbove()
	{
		var gate = new MemorySpikeGate(_env.Settings, _env.Clock);

		Assert.True(gate.ShouldCapture(4096));
		Assert.False(gate.ShouldCapture(4200));
		Assert.False(gate.ShouldCapture(5000));
	}

	[Fact]
	public void SpikeGate_RearmsAfterDropBelowThreshold_ButNotBeforeAnHour()
	{
		var gate = new MemorySpikeGate(_env.Settings, _env.Clock);
		Assert.True(gate.ShouldCapture(4500));

		_env.Clock.Advance(TimeSpan.FromMinutes(5));
		Assert.False(gate.ShouldCapture(1000));
		Assert.False(gate.ShouldCapture(4500));

		_env.Clock.Advance(TimeSpan.FromMinutes(Constants.Diagnostics.SpikeMinIntervalMinutes));
		Assert.True(gate.ShouldCapture(4500));
	}

	[Fact]
	public void SpikeGate_RearmsOnAnotherTwoGigabytesOfGrowth()
	{
		var gate = new MemorySpikeGate(_env.Settings, _env.Clock);
		Assert.True(gate.ShouldCapture(4500));

		_env.Clock.Advance(TimeSpan.FromMinutes(Constants.Diagnostics.SpikeMinIntervalMinutes + 1));

		Assert.False(gate.ShouldCapture(4500 + Constants.Diagnostics.SpikeRearmRiseMb - 1));
		Assert.True(gate.ShouldCapture(4500 + Constants.Diagnostics.SpikeRearmRiseMb));
	}
}
