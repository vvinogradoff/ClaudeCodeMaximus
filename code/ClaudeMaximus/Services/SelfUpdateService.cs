using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ClaudeMaximus.Services;

/// <summary>
/// Detects whether a fresh build is available and spawns an out-of-process
/// copy script so the running directory is updated after the app exits.
/// </summary>
public class SelfUpdateService : ISelfUpdateService
{
	private const string AssemblyFileName = "ClaudeMaximus.dll";
	private const string SolutionFileName = "*.sln";

	private static readonly int[] RetryDelays = { 1, 2, 4, 8, 16, 32, 64 };

	private readonly IAppSettingsService _appSettings;

	public bool IsRunningFromBuildOutput { get; private set; }

	public SelfUpdateService(IAppSettingsService appSettings)
	{
		_appSettings = appSettings;
	}

	public void Initialize()
	{
		var runningDir = AppContext.BaseDirectory.TrimEnd('\\', '/');

		// Auto-detect source location if not configured
		var sourceLocation = _appSettings.Settings.SourceCodesLocation;
		if (string.IsNullOrEmpty(sourceLocation))
		{
			sourceLocation = FindSolutionRoot(runningDir);
			if (sourceLocation != null)
			{
				Log.Information("SelfUpdate: auto-detected source location = {SourceLocation}", sourceLocation);
				_appSettings.Settings.SourceCodesLocation = sourceLocation;
				_appSettings.Save();
			}
		}

		// Check if running from project build output
		if (!string.IsNullOrEmpty(sourceLocation))
		{
			var buildOutputDir = FindBuildOutputDir(sourceLocation);
			if (buildOutputDir != null &&
				runningDir.Equals(buildOutputDir, StringComparison.OrdinalIgnoreCase))
			{
				IsRunningFromBuildOutput = true;
				Log.Warning("SelfUpdate: running from build output ({Dir}), self-update disabled", runningDir);
			}
			else if (buildOutputDir != null && !IsRunningFromBuildOutput)
			{
				// Apply any pending build immediately on startup so the next restart is always current.
				TryApplyBuildNow(buildOutputDir, runningDir);
			}
		}

		Log.Information("SelfUpdate: initialized. RunningDir={RunningDir}, SourceLocation={Source}, RunningFromBuild={Flag}",
			runningDir, sourceLocation ?? "(none)", IsRunningFromBuildOutput);
	}

	public void CheckAndTriggerUpdate()
	{
		if (IsRunningFromBuildOutput)
		{
			Log.Information("SelfUpdate: skipping — running from build output.");
			return;
		}

		try
		{
			var runningDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
			var sourceLocation = _appSettings.Settings.SourceCodesLocation;

			if (string.IsNullOrEmpty(sourceLocation))
			{
				Log.Information("SelfUpdate: no source location configured, skipping.");
				return;
			}

			var buildOutputDir = FindBuildOutputDir(sourceLocation);
			if (buildOutputDir == null)
			{
				Log.Information("SelfUpdate: build output directory not found under {Source}", sourceLocation);
				return;
			}

			var sourceDll  = Path.Combine(buildOutputDir, AssemblyFileName);
			var runningDll = Path.Combine(runningDir, AssemblyFileName);

			Log.Information("SelfUpdate: source = {SourceDir}", buildOutputDir);

			if (!File.Exists(sourceDll))
			{
				Log.Information("SelfUpdate: source dll not found: {SourceDll}", sourceDll);
				return;
			}

			if (!File.Exists(runningDll))
			{
				Log.Information("SelfUpdate: running dll not found: {RunningDll}, spawning copy anyway.", runningDll);
				SpawnCopyProcess(buildOutputDir, runningDir);
				return;
			}

			var srcTime = File.GetLastWriteTimeUtc(sourceDll);
			var runTime = File.GetLastWriteTimeUtc(runningDll);

			if (srcTime <= runTime)
			{
				Log.Information("SelfUpdate: running copy is up-to-date. Source={SrcTime}, Running={RunTime}", srcTime, runTime);
				return;
			}

			Log.Information("SelfUpdate: newer build detected, spawning copy. Source={SrcTime}, Running={RunTime}", srcTime, runTime);
			SpawnCopyProcess(buildOutputDir, runningDir);
		}
		catch (Exception ex)
		{
			Log.Error(ex, "SelfUpdate: unexpected error during update check.");
		}
	}

	/// <summary>
	/// Copies all files from buildOutputDir to runningDir synchronously on startup.
	/// Skips individual files that are locked (e.g. the running .exe), logs each result.
	/// Any locked files are picked up by the exit-time copy as a fallback.
	/// </summary>
	private static void TryApplyBuildNow(string buildOutputDir, string runningDir)
	{
		try
		{
			var sourceDll = Path.Combine(buildOutputDir, AssemblyFileName);
			var runningDll = Path.Combine(runningDir, AssemblyFileName);

			if (!File.Exists(sourceDll))
				return;

			// Skip copy if running copy is already at least as new.
			if (File.Exists(runningDll))
			{
				var srcTime = File.GetLastWriteTimeUtc(sourceDll);
				var runTime = File.GetLastWriteTimeUtc(runningDll);
				if (srcTime <= runTime)
				{
					Log.Information("SelfUpdate: startup check — running copy is current, no copy needed.");
					return;
				}
				Log.Information("SelfUpdate: newer build detected on startup (src={SrcTime}, run={RunTime}), copying now...", srcTime, runTime);
			}
			else
			{
				Log.Information("SelfUpdate: running dll missing, copying build output on startup...");
			}

			var files = Directory.GetFiles(buildOutputDir);
			var copied = 0;
			var skipped = 0;
			foreach (var src in files)
			{
				var dest = Path.Combine(runningDir, Path.GetFileName(src));
				try
				{
					File.Copy(src, dest, overwrite: true);
					copied++;
				}
				catch (Exception ex)
				{
					// File locked by the running process — the exit-time script handles it.
					Log.Warning("SelfUpdate: skipped locked file {File}: {Msg}", Path.GetFileName(src), ex.Message);
					skipped++;
				}
			}

			Log.Information("SelfUpdate: startup copy done — {Copied} copied, {Skipped} skipped (locked).", copied, skipped);
		}
		catch (Exception ex)
		{
			Log.Error(ex, "SelfUpdate: error during startup copy.");
		}
	}

	/// <summary>
	/// Finds the bin/Debug/net* directory for the main ClaudeMaximus project
	/// under the given solution root. Handles platform-specific TFMs like
	/// net9.0-windows10.0.17763.0 by scanning for any net* subdirectory.
	/// </summary>
	private static string? FindBuildOutputDir(string solutionRoot)
	{
		try
		{
			var csprojFiles = Directory.GetFiles(solutionRoot, "ClaudeMaximus.csproj", SearchOption.AllDirectories);

			foreach (var csproj in csprojFiles)
			{
				var projectDir = Path.GetDirectoryName(csproj)!;

				// Skip test projects
				if (projectDir.Contains("Tests", StringComparison.OrdinalIgnoreCase))
					continue;

				var binDebugDir = Path.Combine(projectDir, "bin", "Debug");
				if (!Directory.Exists(binDebugDir))
					return null;

				// Prefer the directory whose ClaudeMaximus.dll is newest (handles
				// both net9.0 and platform-specific TFMs like net9.0-windows10.0.17763.0).
				var tfmDirs = Directory.GetDirectories(binDebugDir, "net*");
				if (tfmDirs.Length == 0)
					return null;

				string? best       = null;
				DateTime bestTime  = DateTime.MinValue;
				foreach (var dir in tfmDirs)
				{
					var dll = Path.Combine(dir, AssemblyFileName);
					if (!File.Exists(dll))
						continue;
					var t = File.GetLastWriteTimeUtc(dll);
					if (t > bestTime) { bestTime = t; best = dir; }
				}

				return best ?? tfmDirs[0];
			}
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "SelfUpdate: error finding build output under {Root}", solutionRoot);
		}

		return null;
	}

	/// <summary>
	/// Walk up the directory tree from startDir until a *.sln file is found.
	/// </summary>
	private static string? FindSolutionRoot(string startDir)
	{
		var current = startDir;
		while (current != null)
		{
			if (Directory.GetFiles(current, SolutionFileName, SearchOption.TopDirectoryOnly).Any())
				return current;

			current = Directory.GetParent(current)?.FullName;
		}

		return null;
	}

	private static void SpawnCopyProcess(string sourceDir, string destDir)
	{
		var scriptPath = Path.Combine(Path.GetTempPath(), "ClaudeMaximus_update.ps1");
		File.WriteAllText(scriptPath, BuildScript(sourceDir, destDir, RetryDelays));

		var psi = new ProcessStartInfo
		{
			FileName        = "powershell.exe",
			Arguments       = $"-ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
			UseShellExecute = true,
			CreateNoWindow  = true,
		};

		Process.Start(psi);
	}

	private static string BuildScript(string sourceDir, string destDir, int[] delays)
	{
		var src  = sourceDir.Replace("'", "''");
		var dst  = destDir.Replace("'", "''");

		return
@$"$source = '{src}'
$dest   = '{dst}'
$delays = @({string.Join(", ", delays)})

for ($i = 0; $i -lt $delays.Length; $i++) {{
    try {{
        $files = Get-ChildItem -Path $source -File
        foreach ($file in $files) {{
            $destFile = Join-Path $dest $file.Name
            Copy-Item -Path $file.FullName -Destination $destFile -Force
        }}
        Write-Host 'ClaudeMaximus update: copy complete.'
        exit 0
    }} catch {{
        $delay = $delays[$i]
        $msg   = $_.Exception.Message
        Write-Host ""ClaudeMaximus update: attempt $($i + 1) failed - $msg. Retrying in $($delay)s...""
        Start-Sleep -Seconds $delay
    }}
}}
Write-Host 'ClaudeMaximus update: all attempts exhausted.'
";
	}
}
