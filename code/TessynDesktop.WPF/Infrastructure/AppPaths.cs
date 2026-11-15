namespace TessynDesktop.WPF.Infrastructure;

/// <summary>Locations below the per-user data root (<c>%AppData%\TessynDesktop</c>); the root is injectable for tests.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AppPaths
{
	public AppPaths() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Constants.Paths.DataFolderName))
	{
	}

	public AppPaths(string dataRoot)
	{
		DataRoot = dataRoot;
	}

	public string DataRoot { get; }

	public string LogsDirectory => Path.Combine(DataRoot, Constants.Paths.LogsFolder);

	public string LogFilePath => Path.Combine(LogsDirectory, Constants.Paths.LogFilePattern);

	public string WebView2UserDataDirectory => Path.Combine(DataRoot, Constants.Paths.WebView2Folder);

	public string ClientStateFilePath => Path.Combine(DataRoot, Constants.Paths.ClientsFolder, Constants.Paths.ClientStateFileName);

	/// <summary>Where preview files (images, video, files to open externally) are written; emptied at startup and served to the preview page as <c>preview.local</c>.</summary>
	public string PreviewCacheDirectory => Path.Combine(DataRoot, Constants.Paths.PreviewCacheFolder);

	/// <summary>The folder with the transcript page, next to the executable.</summary>
	public string WebAssetsDirectory => Path.Combine(AppContext.BaseDirectory, Constants.Paths.WebAssetsFolder);
}
