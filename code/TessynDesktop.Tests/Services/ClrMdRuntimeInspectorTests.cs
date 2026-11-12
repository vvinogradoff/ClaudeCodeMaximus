using System;
using System.IO;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>D1.4/D1.6 smoke tests: the inspector really reads this test process through a ClrMD snapshot. Windows only.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClrMdRuntimeInspectorTests : IDisposable
{
	private readonly DiagnosticsTestEnv _env = new();
	private readonly UiThreadTracker _ui = new();
	private readonly ClrMdRuntimeInspector _inspector;

	public ClrMdRuntimeInspectorTests()
	{
		_ui.CaptureCurrentThread();
		_inspector = new ClrMdRuntimeInspector(_ui, _env.Settings, _env.Sink.CreateLogger());
	}

	public void Dispose() => _env.Dispose();

	[Fact]
	public void CaptureThreadStacks_ReadsThisProcessAndMarksTheUiThread()
	{
		if (!OperatingSystem.IsWindows())
			return;

		// Windows refuses to create a process snapshot now and then ("Could not create snapshot ... Error 6") when the machine
		// is busy: that is the OS, not the inspector, so a refused snapshot is retried a few times.
		var capture = _inspector.CaptureThreadStacks();
		for (var attempt = 1; attempt < 5 && !capture.Success && capture.Text.Contains("snapshot", StringComparison.OrdinalIgnoreCase); attempt++)
		{
			System.Threading.Thread.Sleep(200);
			capture = _inspector.CaptureThreadStacks();
		}

		Assert.True(capture.Success, capture.Text);
		Assert.Contains($"managed thread {Environment.CurrentManagedThreadId} ", capture.Text);
		Assert.Contains("[UI]", capture.Text);
		Assert.NotEmpty(capture.UiThreadFrames);
	}

	[Fact]
	public void WriteHeapHistogram_WritesTypesAndSegmentTotals()
	{
		if (!OperatingSystem.IsWindows())
			return;
		var keepAlive = Enumerable.Range(0, 1000).Select(i => new string('x', 100 + i)).ToList();
		var path = Path.Combine(_env.Folder, "heap-test.txt");
		Directory.CreateDirectory(_env.Folder);

		var outcome = _inspector.WriteHeapHistogram(path);
		for (var attempt = 1; attempt < 5 && !outcome.Success && outcome.Message.Contains("snapshot", StringComparison.OrdinalIgnoreCase); attempt++)
		{
			System.Threading.Thread.Sleep(200);
			outcome = _inspector.WriteHeapHistogram(path);
		}

		Assert.True(outcome.Success, outcome.Message);
		var text = File.ReadAllText(path);
		Assert.Contains("Totals per GC segment kind", text);
		Assert.Contains("Top 60 types by total bytes", text);
		Assert.Contains("Top 60 types by count", text);
		Assert.Contains("System.String", text);
		GC.KeepAlive(keepAlive);
	}

	[Fact]
	public void WriteDump_DumpsDisabled_RefusesWithoutWritingAnything()
	{
		var path = Path.Combine(_env.Folder, "dump-test.dmp");

		var outcome = _inspector.WriteDump(path);

		Assert.False(outcome.Success);
		Assert.Contains("DumpsEnabled", outcome.Message);
		Assert.False(File.Exists(path));
	}
}
