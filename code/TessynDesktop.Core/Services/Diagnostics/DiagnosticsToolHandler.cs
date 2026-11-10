using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Logic behind the <c>diag_snapshot</c>, <c>diag_heap</c> and <c>diag_dump</c> MCP tools (D1.7). Runs on HttpListener
/// threads and must never touch the UI dispatcher, so the tools answer while the UI is frozen.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DiagnosticsToolHandler
{
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	private readonly IProcessMetricsSampler _sampler;
	private readonly IUiHangWatchdog _watchdog;
	private readonly IRuntimeInspector _inspector;
	private readonly IDiagnosticsArtifactStore _artifacts;

	public DiagnosticsToolHandler(
		IProcessMetricsSampler sampler,
		IUiHangWatchdog watchdog,
		IRuntimeInspector inspector,
		IDiagnosticsArtifactStore artifacts)
	{
		_sampler = sampler;
		_watchdog = watchdog;
		_inspector = inspector;
		_artifacts = artifacts;
	}

	public DiagnosticToolResultModel Snapshot()
	{
		var snapshot = new DiagnosticsSnapshotModel(_sampler.GetSnapshot(), _watchdog.GetState());
		return new DiagnosticToolResultModel(false, JsonSerializer.Serialize(snapshot, JsonOptions));
	}

	public DiagnosticToolResultModel Heap()
	{
		var path = _artifacts.CreatePath(DiagnosticArtifactKind.Heap);
		var outcome = _inspector.WriteHeapHistogram(path);
		if (!outcome.Success)
			return new DiagnosticToolResultModel(true, outcome.Message);

		_artifacts.EnforceRetention(DiagnosticArtifactKind.Heap);
		var top = File.ReadLines(path).Take(Constants.Diagnostics.HeapToolPreviewLines);
		return new DiagnosticToolResultModel(false, $"Heap histogram written to {path}{Environment.NewLine}{string.Join(Environment.NewLine, top)}");
	}

	public DiagnosticToolResultModel Dump()
	{
		var path = _artifacts.CreatePath(DiagnosticArtifactKind.Dump);
		var outcome = _inspector.WriteDump(path);
		if (!outcome.Success)
			return new DiagnosticToolResultModel(true, outcome.Message);

		_artifacts.EnforceRetention(DiagnosticArtifactKind.Dump);
		return new DiagnosticToolResultModel(false, $"Process dump written to {path}");
	}
}
