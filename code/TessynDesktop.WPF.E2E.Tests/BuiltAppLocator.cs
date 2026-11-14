namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>Finds the built <c>TessynDesktop.WPF.exe</c> (with its dlls) in the WPF project's own output folder.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class BuiltAppLocator
{
	/// <summary>The newest built exe below the repository's code folder (the E2E project builds the WPF project first).</summary>
	public string FindExe()
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
		{
			var bin = Path.Combine(dir.FullName, "TessynDesktop.WPF", "bin");
			if (!Directory.Exists(bin))
				continue;

			var newest = Directory.EnumerateFiles(bin, "TessynDesktop.WPF.exe", SearchOption.AllDirectories)
				.Where(f => File.Exists(Path.ChangeExtension(f, ".dll")))
				.OrderByDescending(File.GetLastWriteTimeUtc)
				.FirstOrDefault();
			if (newest != null)
				return newest;
		}

		throw new FileNotFoundException("The built TessynDesktop.WPF.exe was not found; build code/TessynDesktop.sln first.");
	}
}
