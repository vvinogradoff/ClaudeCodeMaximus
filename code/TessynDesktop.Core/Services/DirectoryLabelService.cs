using System;
using System.Linq;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class DirectoryLabelService : IDirectoryLabelService
{
	private static readonly char[] Separators = ['\\', '/'];

	/// <summary>
	/// Returns the display label for a directory path (FR.1.3). Pure string logic: no filesystem or git access.
	/// - Takes the last path segment (both separators accepted, trailing separators ignored).
	/// - While that segment is a well-known project folder (Code, Docs, Data) and has a parent that is not a
	///   drive designator, uses the parent instead; repeats.
	/// - A drive root (e.g. "C:\") is returned as "C:\".
	/// </summary>
	public string GetLabel(string directoryPath)
	{
		var segments = directoryPath.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
		if (segments.Length == 0)
			return directoryPath;

		var index = segments.Length - 1;
		while (index > 0
			&& IsWellKnownFolder(segments[index])
			&& !IsDriveDesignator(segments[index - 1]))
			index--;

		var label = segments[index];
		return IsDriveDesignator(label) ? label + '\\' : label;
	}

	private static bool IsWellKnownFolder(string segment)
		=> Constants.WellKnownFolders.All.Contains(segment, StringComparer.OrdinalIgnoreCase);

	private static bool IsDriveDesignator(string segment)
		=> segment.Length == 2 && char.IsAsciiLetter(segment[0]) && segment[1] == ':';
}
