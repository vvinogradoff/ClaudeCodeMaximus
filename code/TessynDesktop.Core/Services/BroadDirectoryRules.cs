using System.Text.RegularExpressions;

namespace TessynDesktop.Services;

/// <summary>
/// What a directory of the session tree may be (J1 review T1). A directory is the working directory of every session below it, and under
/// the A11 default (file access mode WorkingDirectory) a relay client may read what lies under it. So a directory that is too broad (a
/// drive root, the user profile or a parent of it, the app's data root or anything above or below it) is never added by the daemon and
/// never grants reads. Pure string rules first (no disk access, no network call); the disk checks (reparse point, network drive) are separate.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class BroadDirectoryRules
{
	private readonly string[] _dataRoots;
	private readonly string[] _userProfiles;

	/// <summary>The rules for this machine: the data root and the user profile in every form they are known by.</summary>
	public BroadDirectoryRules()
		: this(
			FileAccessPolicy.WithCanonical(Constants.DataRoot),
			FileAccessPolicy.WithCanonical(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
	{
	}

	public BroadDirectoryRules(IReadOnlyList<string> dataRoots, IReadOnlyList<string> userProfiles)
	{
		_dataRoots    = [.. dataRoots.Select(Trim).Where(p => p.Length > 0)];
		_userProfiles = [.. userProfiles.Select(Trim).Where(p => p.Length > 0)];
	}

	[GeneratedRegex(@"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant)]
	private static partial Regex DriveAbsolute();

	/// <summary>
	/// True for a path of the form <c>X:\...</c> (or with forward slashes). Refuses relative and drive-relative paths, UNC (<c>\\host\share</c>),
	/// the device prefixes <c>\\?\</c> and <c>\\.\</c>, and anything with a NUL or a stream/colon after the drive. Looks at the text only.
	/// </summary>
	public bool HasAcceptableShape(string path) =>
		path.Length > 0
		&& !path.StartsWith(@"\\", StringComparison.Ordinal)
		&& !path.StartsWith("//", StringComparison.Ordinal)
		&& DriveAbsolute().IsMatch(path)
		&& path.IndexOf('\0') < 0
		&& path.IndexOf(':', 2) < 0;

	/// <summary>
	/// Resolves <paramref name="path"/> to its full form without a trailing separator, so <c>C:\proj\</c> and <c>C:\proj</c> are one node.
	/// Returns null when the shape is not acceptable or the path cannot be resolved. A drive root keeps its separator (<c>C:\</c>).
	/// </summary>
	public string? Normalize(string path)
	{
		path = path.Trim();
		if (!HasAcceptableShape(path))
			return null;

		string full;
		try
		{
			full = Path.GetFullPath(path);
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			return null;
		}

		// GetFullPath may map a name to a device form; check the shape again on the result.
		if (!HasAcceptableShape(full))
			return null;

		var root = Path.GetPathRoot(full) ?? string.Empty;
		return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
	}

	/// <summary>
	/// True when <paramref name="fullPath"/> is a drive root, the user profile or a parent of it, or the data root or anything above or
	/// below it. A path that is not of the accepted shape counts as too broad (it is nothing a directory may be).
	/// </summary>
	public bool IsTooBroad(string fullPath)
	{
		var normalized = Normalize(fullPath);
		if (normalized == null)
			return true;

		var root = Path.GetPathRoot(normalized) ?? string.Empty;
		if (normalized.Length <= root.Length)
			return true;

		foreach (var profile in _userProfiles)
		{
			if (SamePath(normalized, profile) || IsBelow(profile, normalized))
				return true;
		}

		foreach (var dataRoot in _dataRoots)
		{
			if (SamePath(normalized, dataRoot) || IsBelow(dataRoot, normalized) || IsBelow(normalized, dataRoot))
				return true;
		}

		return false;
	}

	private static string Trim(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

	private static bool SamePath(string a, string b) => string.Equals(Trim(a), Trim(b), StringComparison.OrdinalIgnoreCase);

	/// <summary>True when <paramref name="child"/> lies strictly below <paramref name="parent"/>.</summary>
	private static bool IsBelow(string child, string parent) =>
		Trim(child).StartsWith(Trim(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
