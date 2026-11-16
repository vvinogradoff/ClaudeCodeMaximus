using System.Reflection;

namespace TessynDesktop.WPF.Infrastructure;

/// <summary>The build number the title bar shows: when the running assembly was written, as <c>yyyyMM.dd.HHmm</c> (the Avalonia client's format).</summary>
/// <remarks>Created by Claude</remarks>
public static class BuildNumber
{
	/// <summary>The build number of <paramref name="assembly"/>; empty when its file cannot be found.</summary>
	public static string Read(Assembly assembly)
	{
		try
		{
			var location = assembly.Location;
			return !string.IsNullOrEmpty(location) && File.Exists(location)
				? File.GetLastWriteTime(location).ToString(Constants.TitleBar.BuildNumberFormat)
				: string.Empty;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
		{
			return string.Empty;
		}
	}
}
