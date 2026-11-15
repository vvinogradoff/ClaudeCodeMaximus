namespace TessynDesktop.WPF.ViewModels;

/// <summary>Shortens a git remote URL to the label shown beside a directory (<c>owner/repo</c>).</summary>
/// <remarks>Created by Claude</remarks>
public static class GitOriginText
{
	/// <summary>The last two path segments of the remote without <c>.git</c>; the whole text when it does not look like a URL.</summary>
	public static string Short(string origin)
	{
		var text = origin.Trim().TrimEnd('/', '\\');
		if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
			text = text[..^4];

		var parts = text.Split(['/', ':'], StringSplitOptions.RemoveEmptyEntries);
		return parts.Length >= 2 ? parts[^2] + "/" + parts[^1] : text;
	}
}
