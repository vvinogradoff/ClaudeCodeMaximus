using TessynDesktop.WPF;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.Tests;

/// <summary>Sanity checks of the project skeleton.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ConstantsTests
{
	[Fact]
	public void AppPaths_PlaceLogsAndClientStateBelowTheDataRoot()
	{
		var paths = new AppPaths(@"C:\data\TessynDesktop");

		Assert.Equal(@"C:\data\TessynDesktop\logs\wpf-.txt", paths.LogFilePath);
		Assert.Equal(@"C:\data\TessynDesktop\clients\wpf.json", paths.ClientStateFilePath);
		Assert.Equal(@"C:\data\TessynDesktop\webview2", paths.WebView2UserDataDirectory);
	}

	[Fact]
	public void PageSize_MatchesTheDocumentedHundredEntries()
	{
		Assert.Equal(100, Constants.Limits.EntriesPageSize);
	}
}
