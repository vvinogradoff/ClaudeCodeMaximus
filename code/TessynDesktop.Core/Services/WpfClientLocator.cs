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
