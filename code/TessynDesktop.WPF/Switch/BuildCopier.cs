namespace TessynDesktop.WPF.Switch;

/// <summary>What one copy did.</summary>
/// <param name="Copied">Files written.</param>
/// <param name="Skipped">Files left alone because they were locked or unreadable.</param>
/// <param name="UpToDate">Files already identical (same size and write time).</param>
/// <remarks>Created by Claude</remarks>
public sealed record BuildCopyResult(int Copied, int Skipped, int UpToDate)
{
	/// <summary>True when the destination now matches the source completely.</summary>
	public bool IsComplete => Skipped == 0;
}

/// <summary>
/// Copies a build folder (recursively) into a run folder: a file is written when it is missing or differs in size or write time (the copy keeps the write time, so a
/// second run copies nothing). Nothing is ever deleted from the destination, so a sibling folder such as <c>host</c> is safe. A locked file is skipped and counted.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class BuildCopier
{
	public static BuildCopyResult Copy(string sourceDir, string destinationDir)
	{
		var copied = 0;
		var skipped = 0;
		var upToDate = 0;
		Directory.CreateDirectory(destinationDir);

		foreach (var source in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
		{
			var relative = Path.GetRelativePath(sourceDir, source);
			var target = Path.Combine(destinationDir, relative);
			try
			{
				if (IsSame(source, target))
				{
					upToDate++;
					continue;
				}

				Directory.CreateDirectory(Path.GetDirectoryName(target)!);
				File.Copy(source, target, overwrite: true);
				copied++;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				skipped++;
			}
		}

		return new BuildCopyResult(copied, skipped, upToDate);
	}

	private static bool IsSame(string source, string target)
	{
		if (!File.Exists(target))
			return false;

		var a = new FileInfo(source);
		var b = new FileInfo(target);
		return a.Length == b.Length && a.LastWriteTimeUtc == b.LastWriteTimeUtc;
	}
}
