using System;
using System.IO;
using System.Linq;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class DiagnosticsArtifactStore : IDiagnosticsArtifactStore
{
	private readonly DiagnosticsSettingsModel _settings;
	private readonly TimeProvider _time;
	private readonly ILogger _log;

	public string Folder { get; }

	/// <summary>Production folder: <c>%AppData%\TessynDesktop\diag</c>.</summary>
	public DiagnosticsArtifactStore(DiagnosticsSettingsModel settings, TimeProvider? time = null, ILogger? log = null)
		: this(Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			Constants.AppDataFolderName,
			Constants.Diagnostics.DiagFolderName), settings, time, log)
	{
	}

	public DiagnosticsArtifactStore(string folder, DiagnosticsSettingsModel settings, TimeProvider? time = null, ILogger? log = null)
	{
		Folder = folder;
		_settings = settings;
		_time = time ?? TimeProvider.System;
		_log = log ?? Log.ForContext<DiagnosticsArtifactStore>();
	}

	public string CreatePath(DiagnosticArtifactKind kind)
	{
		Directory.CreateDirectory(Folder);
		var stamp = _time.GetLocalNow().ToString(Constants.Diagnostics.ArtifactTimestampFormat);
		return Path.Combine(Folder, Prefix(kind) + stamp + Extension(kind));
	}

	public void EnforceRetention(DiagnosticArtifactKind kind)
	{
		if (!Directory.Exists(Folder))
			return;

		var keep = Math.Max(0, kind == DiagnosticArtifactKind.Dump ? _settings.DumpRetentionCount : _settings.ArtifactRetentionCount);
		var stale = new DirectoryInfo(Folder)
			.GetFiles(Prefix(kind) + "*" + Extension(kind))
			.OrderByDescending(f => f.LastWriteTimeUtc)
			.ThenByDescending(f => f.Name, StringComparer.Ordinal)
			.Skip(keep);

		foreach (var file in stale)
		{
			try
			{
				file.Delete();
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "Could not delete old diagnostics file {Path}", file.FullName);
			}
		}
	}

	private static string Prefix(DiagnosticArtifactKind kind) => kind switch
	{
		DiagnosticArtifactKind.Hang => Constants.Diagnostics.HangFilePrefix,
		DiagnosticArtifactKind.Heap => Constants.Diagnostics.HeapFilePrefix,
		_ => Constants.Diagnostics.DumpFilePrefix,
	};

	private static string Extension(DiagnosticArtifactKind kind) =>
		kind == DiagnosticArtifactKind.Dump ? Constants.Diagnostics.DumpArtifactExtension : Constants.Diagnostics.TextArtifactExtension;
}
