namespace TessynDesktop.WPF.Tests;

/// <summary>Finds the installed Microsoft Edge, which the browser layout tests drive headless.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class EdgeBrowser
{
	private static readonly string[] Paths =
	[
		@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
		@"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
	];

	/// <summary>The path of msedge.exe; null when Edge is not installed.</summary>
	public string? Find() => Paths.FirstOrDefault(File.Exists);
}