using System.Xml.Linq;

namespace TessynDesktop.WPF.Switch;

/// <summary>
/// The daemon's source paths (FR.32.9): the host project and every project it references, transitively, read from the <c>ProjectReference</c> items, plus the build files under
/// <c>code\</c> that affect them. Paths are git pathspecs relative to the source location, with forward slashes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonSourcePathResolver
{
	/// <summary>The pathspecs, or null when the host project or a reference cannot be read.</summary>
	public IReadOnlyList<string>? Resolve(string sourceLocation)
	{
		var codeDirectory = Path.Combine(sourceLocation, Constants.Switch.CodeFolder);
		var hostProject = ProjectFile(codeDirectory);
		if (!File.Exists(hostProject))
		{
			codeDirectory = sourceLocation; // the source location may be the code folder itself
			hostProject = ProjectFile(codeDirectory);
		}

		var projects = new List<string>();
		if (!Collect(hostProject, projects))
			return null;

		var root = Path.GetFullPath(sourceLocation);
		var paths = new List<string>();
		foreach (var project in projects)
		{
			var relative = RelativeSpec(root, Path.GetDirectoryName(project)!);
			if (relative == null)
				return null;

			paths.Add(relative);
		}

		var codeRelative = RelativeSpec(root, codeDirectory);
		if (codeRelative == null)
			return null;

		foreach (var pattern in Constants.Switch.DaemonBuildFilePatterns)
			paths.Add(codeRelative.Length == 0 ? pattern : codeRelative + "/" + pattern);

		return paths;
	}

	private static string ProjectFile(string codeDirectory) =>
		Path.Combine(codeDirectory, Constants.Switch.HostProjectFolder, Constants.Switch.HostProjectFolder + Constants.Switch.ProjectFileExtension);

	/// <summary>Adds <paramref name="project"/> and what it references to <paramref name="projects"/> (each once); false when a project file is missing or unreadable.</summary>
	private static bool Collect(string project, List<string> projects)
	{
		var full = Path.GetFullPath(project);
		if (projects.Contains(full, StringComparer.OrdinalIgnoreCase))
			return true;

		if (!File.Exists(full))
			return false;

		projects.Add(full);
		List<string> includes;
		try
		{
			includes = XDocument.Load(full).Descendants()
				.Where(element => element.Name.LocalName == Constants.Switch.ProjectReferenceElement)
				.Select(element => (string?)element.Attribute(Constants.Switch.IncludeAttribute))
				.Where(include => !string.IsNullOrWhiteSpace(include))
				.Select(include => include!)
				.ToList();
		}
		catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
		{
			return false;
		}

		var directory = Path.GetDirectoryName(full)!;
		return includes.All(include => Collect(Path.Combine(directory, include.Replace('\\', Path.DirectorySeparatorChar)), projects));
	}

	/// <summary>The directory relative to the root with forward slashes ("" for the root itself); null when it is outside the root.</summary>
	private static string? RelativeSpec(string root, string directory)
	{
		var relative = Path.GetRelativePath(root, Path.GetFullPath(directory));
		if (relative == ".")
			return "";

		return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? null : relative.Replace('\\', '/');
	}
}
