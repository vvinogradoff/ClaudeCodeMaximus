using System.IO;

namespace TessynDesktop.Services;

/// <summary>
/// Finds the WPF client's executable for the "start WPF" button of the Avalonia window: the <c>[Publish-WPF]</c> run folder is a
/// sibling of the folder the Avalonia app runs from (<c>[Publish]</c>).
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class WpfClientLocator
{
	public const string RunFolderName = "[Publish-WPF]";
	public const string ExeName = "TessynDesktop.WPF.exe";
	public const string ScriptName = "run-wpf.ps1";

	/// <summary>
	/// <c>run-wpf.ps1</c> in the repository root, the folder that holds the run folders, or null when it is not there. It builds
	/// the WPF client and its host, copies them into <c>[Publish-WPF]</c> and starts the client, so the button always launches the latest build.
	/// </summary>
	public static string? FindScript(string appBaseDirectory)
	{
		if (string.IsNullOrWhiteSpace(appBaseDirectory))
			return null;

		var parent = new DirectoryInfo(Path.TrimEndingDirectorySeparator(appBaseDirectory)).Parent;
		if (parent == null)
			return null;

		var script = Path.Combine(parent.FullName, ScriptName);
		return File.Exists(script) ? script : null;
	}

	/// <summary>
	/// The PowerShell command line that runs <paramref name="scriptPath"/> and keeps the window open with the error when it fails.
	/// Single quotes in the path are doubled for the PowerShell literal string; the path itself may hold brackets.
	/// </summary>
	public static string BuildScriptArguments(string scriptPath)
	{
		var literal = scriptPath.Replace("'", "''");
		return "-NoProfile -ExecutionPolicy Bypass -Command \"try { & '" + literal + "' } catch { Write-Host $_ -ForegroundColor Red; Read-Host 'Starting the WPF client failed. Press Enter to close' }\"";
	}

	/// <summary>The WPF client's exe next to <paramref name="appBaseDirectory"/>, or null when it is not there.</summary>
	public static string? Find(string appBaseDirectory)
	{
		if (string.IsNullOrWhiteSpace(appBaseDirectory))
			return null;

		var current = new DirectoryInfo(Path.TrimEndingDirectorySeparator(appBaseDirectory));
		var parent = current.Parent;
		if (parent == null)
			return null;

		var exe = Path.Combine(parent.FullName, RunFolderName, ExeName);
		return File.Exists(exe) ? exe : null;
	}
}
