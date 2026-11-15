using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The preview cache: files the client wrote for previews (images, video, files to open in the default app), one folder per load,
/// below <see cref="AppPaths.PreviewCacheDirectory"/>. File names are reduced to a safe leaf; the type allowlist is enforced by
/// the caller before anything is written. The cache is emptied at startup and a load's folder is deleted when it is replaced.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewFileStore
{
	private const string FallbackName = "file";

	private readonly AppPaths _paths;

	public PreviewFileStore(AppPaths paths)
	{
		_paths = paths;
	}

	/// <summary>Deletes everything left from an earlier run.</summary>
	public void Clean()
	{
		try
		{
			if (Directory.Exists(_paths.PreviewCacheDirectory))
				Directory.Delete(_paths.PreviewCacheDirectory, recursive: true);
		}
		catch (IOException)
		{
			// A WebView2 helper may still hold a file; the folder is emptied again next start.
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	/// <summary>Creates the folder of a new load and returns the path of its file, named after <paramref name="name"/>.</summary>
	public string NewFilePath(string name)
	{
		var folder = Path.Combine(_paths.PreviewCacheDirectory, Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		return Path.Combine(folder, SafeLeaf(name));
	}

	/// <summary>Deletes the folder of a load (best effort).</summary>
	public void Delete(string? filePath)
	{
		if (filePath == null)
			return;

		try
		{
			var folder = Path.GetDirectoryName(Path.GetFullPath(filePath));
			var root = Path.GetFullPath(_paths.PreviewCacheDirectory) + Path.DirectorySeparatorChar;
			if (folder != null && (folder + Path.DirectorySeparatorChar).StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(folder))
				Directory.Delete(folder, recursive: true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	/// <summary>The URL under which the preview page reaches <paramref name="filePath"/>.</summary>
	public string UrlOf(string filePath)
	{
		var relative = Path.GetRelativePath(_paths.PreviewCacheDirectory, filePath).Replace('\\', '/');
		return $"https://{Constants.Hosts.PreviewHost}/" + string.Join('/', relative.Split('/').Select(Uri.EscapeDataString));
	}

	private static string SafeLeaf(string name)
	{
		var leaf = name[(name.LastIndexOfAny(['\\', '/']) + 1)..];
		foreach (var bad in Path.GetInvalidFileNameChars())
			leaf = leaf.Replace(bad, '_');

		leaf = leaf.Trim().TrimEnd('.', ' ');
		return leaf.Length == 0 ? FallbackName : leaf.Length > 120 ? leaf[^120..] : leaf;
	}
}
