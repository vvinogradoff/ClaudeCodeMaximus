using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace TessynDesktop.Services;

/// <summary>
/// Detects whether a fresh build is available and spawns an out-of-process
/// copy script so the running directory is updated after the app exits.
/// </summary>
public class SelfUpdateService : ISelfUpdateService
{
	private const string AssemblyFileName = "TessynDesktop.dll";
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
	/// Finds the bin/Debug/net9.0 directory for the main TessynDesktop project
	/// under the given solution root.
	/// </summary>
	private static string? FindBuildOutputDir(string solutionRoot)
	{
		try
		{
			var csprojFiles = Directory.GetFiles(solutionRoot, "TessynDesktop.csproj", SearchOption.AllDirectories);

			foreach (var csproj in csprojFiles)
			{
				var projectDir = Path.GetDirectoryName(csproj)!;

				// Skip test projects
				if (projectDir.Contains("Tests", StringComparison.OrdinalIgnoreCase))
					continue;

				var binDebugDir = Path.Combine(projectDir, "bin", "Debug", "net9.0");
				if (Directory.Exists(binDebugDir))
					return binDebugDir;

				// If the directory doesn't exist yet, still return the expected path
				return binDebugDir;
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
		var scriptPath = Path.Combine(Path.GetTempPath(), "TessynDesktop_update.ps1");
		File.WriteAllText(scriptPath, BuildScript(sourceDir, destDir, RetryDelays));

		var psi = new ProcessStartInfo
		{
			FileName        = "powershell.exe",
			Arguments       = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
			UseShellExecute = true,
			CreateNoWindow  = false,
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
        Write-Host 'TessynDesktop update: copy complete.'
        exit 0
    }} catch {{
        $delay = $delays[$i]
        $msg   = $_.Exception.Message
        Write-Host ""TessynDesktop update: attempt $($i + 1) failed - $msg. Retrying in $($delay)s...""
        Start-Sleep -Seconds $delay
    }}
}}
Write-Host 'TessynDesktop update: all attempts exhausted.'
";
	}
}
