using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TessynDesktop.Services;

/// <summary>The type class of a file for the remote read policy.</summary>
/// <remarks>Created by Claude</remarks>
public enum FileTypeClass
{
	/// <summary>Not on the allowlist: relay clients may not read it.</summary>
	Other,
	Text,
	Image,
	Video,
	Pdf,
}

/// <summary>
/// The rules of the remote file reads that depend only on a path: which types exist on the allowlist (md, txt, code, json, images,
/// video, pdf) and which paths are never readable (credential and token stores). Pure; the per-transport rules (mode, size caps) live
/// in <see cref="FileAccessService"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class FileAccessPolicy
{
	private static readonly Dictionary<string, (FileTypeClass Class, string Mime)> Types = BuildTypes();

	private static readonly HashSet<string> DeniedDirectories = new(StringComparer.OrdinalIgnoreCase)
	{
		".ssh", ".gnupg", ".aws", ".azure", ".kube", ".docker", ".claude", ".mozilla",
		// Browser profile folders (Chrome, Edge, Brave, Opera keep cookies and logins below "User Data" or their vendor folder).
		"User Data", "BraveSoftware", "Opera Software",
	};

	/// <summary>Credential locations that are named by two consecutive folders.</summary>
	private static readonly (string Parent, string Child)[] DeniedDirectoryPairs =
	[
		(".config", "gh"), (".config", "gcloud"), (".config", "git"),
		("Microsoft", "Credentials"), ("Microsoft", "Protect"), ("Microsoft", "Vault"),
		("Mozilla", "Firefox"),
	];

	/// <summary>The only folder of the data root a relay client may read from (step 3 stores attachments there).</summary>
	public const string DataRootAttachmentsFolder = "attachments";

	private static readonly HashSet<string> DeniedExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".pem", ".pfx", ".p12", ".key", ".ppk", ".kdbx", ".jks", ".keystore", ".dat",
	};

	private static readonly HashSet<string> DeniedNames = new(StringComparer.OrdinalIgnoreCase)
	{
		".env", ".netrc", ".npmrc", ".pypirc", ".git-credentials", ".credentials.json", "secrets.json", "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519",
	};

	/// <summary>Files of the data root that hold secrets (the settings carry the daemon token) and folders of token stores.</summary>
	private static readonly HashSet<string> DeniedDataRootFiles = new(StringComparer.OrdinalIgnoreCase)
	{
		"appsettings.json", "daemon.json",
	};

	private static readonly HashSet<string> DeniedDataRootDirectories = new(StringComparer.OrdinalIgnoreCase)
	{
		"mcp-tokens", "mcp", "profiles", "clients",
	};

	/// <summary>Type class and MIME type from the extension; <see cref="FileTypeClass.Other"/> with a generic MIME type for the rest.</summary>
	public static (FileTypeClass Class, string Mime) Classify(string path)
	{
		var extension = Path.GetExtension(path);
		return Types.TryGetValue(extension, out var known) ? known : (FileTypeClass.Other, "application/octet-stream");
	}

	/// <summary>
	/// True for a path nobody may read through the API: anything inside a credential folder (<c>.ssh</c>, <c>.aws</c>, ...), key and
	/// token files, credential files, <c>*.dat</c>, and the secret-bearing parts of the app's data root.
	/// </summary>
	/// <param name="fullPath">A full path; the caller normalises it.</param>
	/// <param name="dataRoot">The app's data root (<c>%AppData%\TessynDesktop</c>).</param>
	/// <param name="relay">
	/// True for a relay (web) client: the whole data root is then closed, except its <c>attachments</c> folder. For a loopback client
	/// only the secret-bearing parts of the data root are.
	/// </param>
	public static bool IsDenied(string fullPath, string dataRoot, bool relay = false) => IsDenied(fullPath, [dataRoot], relay);

	/// <inheritdoc cref="IsDenied(string, string, bool)" />
	/// <param name="dataRoots">The data root in every form it is known by (configured and canonical, see <see cref="WithCanonical"/>).</param>
	public static bool IsDenied(string fullPath, IReadOnlyList<string> dataRoots, bool relay)
	{
		var segments = fullPath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
		if (segments.Length == 0)
			return true;

		var folders = segments.Take(segments.Length - 1).ToArray();
		if (folders.Any(DeniedDirectories.Contains))
			return true;

		for (var i = 0; i + 1 < folders.Length; i++)
		{
			foreach (var (parent, child) in DeniedDirectoryPairs)
			{
				if (string.Equals(folders[i], parent, StringComparison.OrdinalIgnoreCase)
				    && string.Equals(folders[i + 1], child, StringComparison.OrdinalIgnoreCase))
					return true;
			}
		}

		if (NameDenied(segments[^1]))
			return true;

		foreach (var dataRoot in dataRoots)
		{
			if (DataRootDenied(fullPath, dataRoot, relay))
				return true;
		}

		return false;
	}

	private static bool DataRootDenied(string fullPath, string dataRoot, bool relay)
	{
		var root = dataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (root.Length > 0 && fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			var inner = fullPath[(root.Length + 1)..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
			if (relay && !(inner.Length > 1 && string.Equals(inner[0], DataRootAttachmentsFolder, StringComparison.OrdinalIgnoreCase)))
				return true;

			if (inner.Length == 1 && DeniedDataRootFiles.Contains(inner[0]))
				return true;

			if (inner.Length > 1 && DeniedDataRootDirectories.Contains(inner[0]))
				return true;
		}

		return false;
	}

	/// <summary>
	/// The directory in every form the rules must know it by: as configured, and its canonical final path (links, junctions and short
	/// names resolved through a directory handle). Done once at startup; a directory that does not exist yet keeps only its configured form.
	/// </summary>
	public static string[] WithCanonical(string directory)
	{
		var canonical = SafeFilePath.CanonicalDirectory(directory);
		return canonical == null || string.Equals(canonical, directory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
			? [directory]
			: [directory, canonical];
	}

	private static bool NameDenied(string name) =>
		DeniedNames.Contains(name)
		|| DeniedExtensions.Contains(Path.GetExtension(name))
		|| name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
		|| name.Contains("credentials", StringComparison.OrdinalIgnoreCase)
		|| name.StartsWith("id_rsa", StringComparison.OrdinalIgnoreCase)
		|| name.StartsWith("id_ed25519", StringComparison.OrdinalIgnoreCase)
		// Claude Code global state (.claude.json, .claude.json.backup, ...): account data and MCP server configs that can hold keys.
		|| name.StartsWith(".claude.json", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// True for a Markdown file directly inside a plans folder of a Claude config directory: <c>&lt;user profile&gt;\.claude\plans</c> or
	/// <c>&lt;data root&gt;\profiles\&lt;id&gt;\plans</c>, no subfolders. This is where plan mode writes the plans a user approves.
	/// </summary>
	/// <param name="finalPath">The real path of the open file.</param>
	public static bool IsPlanFile(string finalPath, string dataRoot, string userProfile) => IsPlanFile(finalPath, [dataRoot], [userProfile]);

	/// <inheritdoc cref="IsPlanFile(string, string, string)" />
	public static bool IsPlanFile(string finalPath, IReadOnlyList<string> dataRoots, IReadOnlyList<string> userProfiles)
	{
		if (!string.Equals(Path.GetExtension(finalPath), ".md", StringComparison.OrdinalIgnoreCase))
			return false;

		return userProfiles.Any(profile => DirectlyIn(finalPath, Path.Combine(profile, ".claude", "plans")))
		       || dataRoots.Any(root => IsProfilePlan(finalPath, root));
	}

	private static bool DirectlyIn(string path, string folder)
	{
		if (folder.Length == 0 || Path.GetFileName(folder).Length == 0)
			return false;

		var directory = Path.GetDirectoryName(path);
		return directory != null
		       && string.Equals(directory.TrimEnd(Path.DirectorySeparatorChar), folder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsProfilePlan(string path, string dataRoot)
	{
		var profiles = Path.Combine(dataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), "profiles") + Path.DirectorySeparatorChar;
		if (!path.StartsWith(profiles, StringComparison.OrdinalIgnoreCase))
			return false;

		var inner = path[profiles.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
		return inner.Length == 3 && string.Equals(inner[1], "plans", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// The deny decision for an open file. One exception to the folder rules: a referenced, single-link Markdown file that lies directly in
	/// a plans folder (<see cref="IsPlanFile"/>) and was reached without a link (the path as named is the real path). Name rules still apply.
	/// </summary>
	public static bool IsDeniedOpened(string namedPath, string finalPath, int linkCount, bool referenced, string dataRoot, string userProfile, bool relay) =>
		IsDeniedOpened(namedPath, finalPath, linkCount, referenced, [dataRoot], [userProfile], relay);

	/// <inheritdoc cref="IsDeniedOpened(string, string, int, bool, string, string, bool)" />
	public static bool IsDeniedOpened(string namedPath, string finalPath, int linkCount, bool referenced, IReadOnlyList<string> dataRoots, IReadOnlyList<string> userProfiles, bool relay)
	{
		if (referenced && linkCount == 1
		    && string.Equals(namedPath, finalPath, StringComparison.OrdinalIgnoreCase)
		    && IsPlanFile(finalPath, dataRoots, userProfiles))
			return NameDenied(Path.GetFileName(finalPath));

		return IsDenied(namedPath, dataRoots, relay) || IsDenied(finalPath, dataRoots, relay);
	}

	private static Dictionary<string, (FileTypeClass, string)> BuildTypes()
	{
		var map = new Dictionary<string, (FileTypeClass, string)>(StringComparer.OrdinalIgnoreCase);

		foreach (var ext in new[] { ".md", ".markdown", ".txt", ".log", ".csv", ".tsv", ".diff", ".patch" })
			map[ext] = (FileTypeClass.Text, ext is ".md" or ".markdown" ? "text/markdown" : "text/plain");

		map[".json"]  = (FileTypeClass.Text, "application/json");
		map[".jsonl"] = (FileTypeClass.Text, "application/x-ndjson");
		map[".xml"]   = (FileTypeClass.Text, "application/xml");
		map[".html"]  = (FileTypeClass.Text, "text/plain");   // active content is served as plain text, never as html
		map[".htm"]   = (FileTypeClass.Text, "text/plain");
		map[".css"]   = (FileTypeClass.Text, "text/css");
		map[".yaml"]  = (FileTypeClass.Text, "application/yaml");
		map[".yml"]   = (FileTypeClass.Text, "application/yaml");

		// Code and configuration, shown as plain text.
		foreach (var ext in new[]
		{
			".cs", ".csproj", ".sln", ".props", ".targets", ".xaml", ".axaml", ".razor", ".cshtml", ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs",
			".scss", ".less", ".vue", ".svelte", ".py", ".go", ".rs", ".java", ".kt", ".swift", ".rb", ".php", ".c", ".h", ".cpp", ".hpp", ".cc",
			".sh", ".ps1", ".psm1", ".bat", ".cmd", ".sql", ".toml", ".ini", ".cfg", ".conf", ".editorconfig", ".gitignore", ".dockerfile", ".proto", ".graphql",
		})
			map[ext] = (FileTypeClass.Text, "text/plain");

		map[".png"]  = (FileTypeClass.Image, "image/png");
		map[".jpg"]  = (FileTypeClass.Image, "image/jpeg");
		map[".jpeg"] = (FileTypeClass.Image, "image/jpeg");
		map[".gif"]  = (FileTypeClass.Image, "image/gif");
		map[".webp"] = (FileTypeClass.Image, "image/webp");
		map[".bmp"]  = (FileTypeClass.Image, "image/bmp");
		map[".svg"]  = (FileTypeClass.Text, "text/plain");   // svg can carry script: text, not an image

		map[".mp4"]  = (FileTypeClass.Video, "video/mp4");
		map[".m4v"]  = (FileTypeClass.Video, "video/mp4");
		map[".webm"] = (FileTypeClass.Video, "video/webm");
		map[".mov"]  = (FileTypeClass.Video, "video/quicktime");
		map[".mkv"]  = (FileTypeClass.Video, "video/x-matroska");
		map[".avi"]  = (FileTypeClass.Video, "video/x-msvideo");
		map[".wmv"]  = (FileTypeClass.Video, "video/x-ms-wmv");

		map[".pdf"] = (FileTypeClass.Pdf, "application/pdf");
		return map;
	}
}
