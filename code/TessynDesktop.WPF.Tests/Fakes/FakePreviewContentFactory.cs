using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>A preview surface that records its disposal.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TrackedPreviewSurface : IPreviewSurface, IDisposable
{
	public bool Disposed { get; private set; }

	public event EventHandler<string>? LinkClicked
	{
		add { }
		remove { }
	}

	public event EventHandler<PreviewRenderedEventArgs>? Rendered
	{
		add { }
		remove { }
	}

	public void ShowText(string text)
	{
	}

	public void ShowMarkdown(string html)
	{
	}

	public void ShowImage(string url)
	{
	}

	public void ShowVideo(string url)
	{
	}

	public void Clear()
	{
	}

	public void Dispose() => Disposed = true;
}

/// <summary>A browser surface that records its disposal.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TrackedBrowserSurface : IBrowserSurface, IDisposable
{
	public bool Disposed { get; private set; }

	public bool CanGoBack => false;

	public bool CanGoForward => false;

	public string? Source { get; set; }

	public string? DocumentTitle { get; set; }

	public List<string> Navigated { get; } = [];

	public event EventHandler<BrowserStateChangedEventArgs>? StateChanged;

	public event EventHandler<string>? NavigationFailed;

	public event EventHandler? BrowserLost
	{
		add { }
		remove { }
	}

	public void Navigate(string url) => Navigated.Add(url);

	public void GoBack()
	{
	}

	public void GoForward()
	{
	}

	public void Reload()
	{
	}

	public void Stop()
	{
	}

	public void RaiseState(bool isLoading = false) => StateChanged?.Invoke(this, new BrowserStateChangedEventArgs(isLoading));

	public void RaiseFailed(string status) => NavigationFailed?.Invoke(this, status);

	public void Dispose() => Disposed = true;
}

/// <summary>Builds the content of a preview window over fake surfaces and counts what it built.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakePreviewContentFactory : IPreviewContentFactory, IDisposable
{
	private readonly TempAppPaths _temp = new();

	public List<TrackedPreviewSurface> FileSurfaces { get; } = [];

	public List<TrackedBrowserSurface> BrowserSurfaces { get; } = [];

	public List<ITessynClient> Clients { get; } = [];

	public FilePreviewViewModel CreateFile(ITessynClient client)
	{
		var surface = new TrackedPreviewSurface();
		FileSurfaces.Add(surface);
		Clients.Add(client);
		var policy = new ExternalOpenPolicy();
		var store = new PreviewFileStore(_temp.Paths);
		return new FilePreviewViewModel(
			new FilePreviewLoader(client, new FileKindClassifier(), store, policy), surface, new MarkdigEntryRenderer(new UrlPolicy()), new FakeExternalFileLauncher(),
			policy, store, new FakeUrlLauncher(), new ImmediateUiDispatcher(), NullLogger<FilePreviewViewModel>.Instance, ownsSurface: true);
	}

	public PreviewBrowserContent CreateBrowser()
	{
		var surface = new TrackedBrowserSurface();
		BrowserSurfaces.Add(surface);
		var viewModel = new BrowserPreviewViewModel(surface, new UrlPolicy(), new FakeUrlLauncher(), new ImmediateUiDispatcher(), NullLogger<BrowserPreviewViewModel>.Instance);
		return new PreviewBrowserContent(viewModel, surface);
	}

	public void Dispose() => _temp.Dispose();
}
