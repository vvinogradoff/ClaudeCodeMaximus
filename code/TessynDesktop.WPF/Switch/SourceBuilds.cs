using System.Text.Json;

namespace TessynDesktop.WPF.Switch;

/// <summary>
/// Finds the newest build of a project under the source tree (<c>SourceCodesLocation</c>, as the Avalonia <c>SelfUpdateService</c> does for <c>[Publish]</c>).
/// The location comes from the daemon's <c>appsettings.json</c>; it must point at the tree that contains <c>code\&lt;Project&gt;</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SourceBuilds
{
	private readonly string _appSettingsPath;

	public SourceBuilds(string appSettingsPath)
	{
		_appSettingsPath = appSettingsPath;
	}

	/// <summary>The configured source location, or null when there is none or the file cannot be read.</summary>
	public string? ReadSourceLocation()
	{
		try
		{
			if (!File.Exists(_appSettingsPath))
				return null;

			using var stream = new FileStream(_appSettingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using var document = JsonDocument.Parse(stream);
			foreach (var property in document.RootElement.EnumerateObject())
			{
				if (string.Equals(property.Name, Constants.Switch.SourceCodesLocationProperty, StringComparison.OrdinalIgnoreCase)
					&& property.Value.ValueKind == JsonValueKind.String
					&& !string.IsNullOrWhiteSpace(property.Value.GetString()))
					return property.Value.GetString();
			}
		}
		catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
		{
		}

		return null;
	}

	/// <summary>
	/// The build folder (<c>bin\Debug\net*</c> or <c>bin\Release\net*</c>) of <paramref name="projectFolder"/> whose <paramref name="assemblyName"/> is the newest, or null.
	/// Looks in <c>&lt;source&gt;\code\&lt;project&gt;</c> first and in <c>&lt;source&gt;\&lt;project&gt;</c> (a source location that is the <c>code</c> folder itself).
	/// </summary>
	public static string? FindNewestBuild(string sourceLocation, string projectFolder, string assemblyName)
	{
		string? best = null;
		var bestTime = DateTime.MinValue;
		foreach (var projectDir in new[] { Path.Combine(sourceLocation, Constants.Switch.CodeFolder, projectFolder), Path.Combine(sourceLocation, projectFolder) })
		{
			foreach (var configuration in new[] { "Debug", "Release" })
			{
				var bin = Path.Combine(projectDir, "bin", configuration);
				if (!Directory.Exists(bin))
					continue;

				foreach (var tfmDir in Directory.GetDirectories(bin, "net*"))
				{
					var assembly = Path.Combine(tfmDir, assemblyName);
					if (!File.Exists(assembly))
						continue;

					var time = File.GetLastWriteTimeUtc(assembly);
					if (time > bestTime)
					{
						bestTime = time;
						best = tfmDir;
					}
				}
			}
		}

		return best;
	}

	/// <summary>True when <paramref name="folder"/> is (inside) a <c>bin\Debug</c> or <c>bin\Release</c> folder, i.e. a build output: nothing runs from there (rule A15).</summary>
	public static bool IsBuildOutput(string folder)
	{
		var parts = Path.GetFullPath(folder).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		for (var i = 0; i < parts.Length - 1; i++)
		{
			if (string.Equals(parts[i], "bin", StringComparison.OrdinalIgnoreCase)
				&& (string.Equals(parts[i + 1], "Debug", StringComparison.OrdinalIgnoreCase) || string.Equals(parts[i + 1], "Release", StringComparison.OrdinalIgnoreCase)))
				return true;
		}

		return false;
	}
}
