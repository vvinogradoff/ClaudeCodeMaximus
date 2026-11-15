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
		".ssh", ".gnupg", ".aws", ".azure", ".kube", ".docker",
	};

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
	public static bool IsDenied(string fullPath, string dataRoot)
	{
		var segments = fullPath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
		if (segments.Length == 0)
			return true;

		if (segments.Take(segments.Length - 1).Any(DeniedDirectories.Contains))
			return true;

		var name = segments[^1];
		if (DeniedNames.Contains(name) || DeniedExtensions.Contains(Path.GetExtension(name)))
			return true;

		if (name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
		    || name.Contains("credentials", StringComparison.OrdinalIgnoreCase)
		    || name.StartsWith("id_rsa", StringComparison.OrdinalIgnoreCase)
		    || name.StartsWith("id_ed25519", StringComparison.OrdinalIgnoreCase))
			return true;

		var root = dataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (root.Length > 0 && fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			var inner = fullPath[(root.Length + 1)..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
			if (inner.Length == 1 && DeniedDataRootFiles.Contains(inner[0]))
				return true;

			if (inner.Length > 1 && DeniedDataRootDirectories.Contains(inner[0]))
				return true;
		}

		return false;
	}

	private static Dictionary<string, (FileTypeClass, string)> BuildTypes()
	{
		var map = new Dictionary<string, (FileTypeClass, string)>(StringComparer.OrdinalIgnoreCase);

		foreach (var ext in new[] { ".md", ".markdown", ".txt", ".log", ".csv", ".tsv", ".diff", ".patch" })
			map[ext] = (FileTypeClass.Text, ext is ".md" or ".markdown" ? "text/markdown" : "text/plain");

		map[".json"]  = (FileTypeClass.Text, "application/json");
		map[".jsonl"] = (FileTypeClass.Text, "application/x-ndjson");
		map[".xml"]   = (FileTypeClass.Text, "application/xml");
		map[".html"]  = (FileTypeClass.Text, "text/html");
		map[".htm"]   = (FileTypeClass.Text, "text/html");
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
		map[".svg"]  = (FileTypeClass.Image, "image/svg+xml");

		map[".mp4"]  = (FileTypeClass.Video, "video/mp4");
		map[".m4v"]  = (FileTypeClass.Video, "video/mp4");
		map[".webm"] = (FileTypeClass.Video, "video/webm");
		map[".mov"]  = (FileTypeClass.Video, "video/quicktime");
		map[".mkv"]  = (FileTypeClass.Video, "video/x-matroska");

		map[".pdf"] = (FileTypeClass.Pdf, "application/pdf");
		return map;
	}
}
