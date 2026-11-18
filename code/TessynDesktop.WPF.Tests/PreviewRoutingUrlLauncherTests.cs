using TessynDesktop.Client;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>The URL launcher that offers every address to the preview router first (FR.29.7).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewRoutingUrlLauncherTests
{
	private readonly FakePreviewRouter _router = new();
	private readonly FakeUrlLauncher _fallback = new();
	private readonly PreviewRoutingUrlLauncher _launcher;

	public PreviewRoutingUrlLauncherTests() => _launcher = new PreviewRoutingUrlLauncher(_router, _fallback, new UrlPolicy());

	[Fact]
	public void RoutedAddress_GoesToTheRouter_WithTheCurrentSession_AndNotToTheBrowser()
	{
		_router.TakesUrls = true;
		_router.CurrentNodeId = "n7";

		Assert.True(_launcher.TryOpen("https://example.com/a?b=1"));

		var item = Assert.Single(_router.Urls);
		Assert.Equal(PreviewItemKind.Url, item.Kind);
		Assert.Equal("https://example.com/a?b=1", item.Url);
		Assert.Equal("n7", item.SourceNodeId);
		Assert.Empty(_fallback.Opened);
	}

	[Fact]
	public void AddressTheRouterRefuses_GoesToTheFallback()
	{
		_router.TakesUrls = false;

		Assert.True(_launcher.TryOpen("https://example.com/a"));

		Assert.Single(_router.Urls);
		Assert.Equal(["https://example.com/a"], _fallback.Opened);
	}

	[Theory]
	[InlineData("ftp://example.com/a")]
	[InlineData("javascript:alert(1)")]
	[InlineData("mailto:someone@example.com")]
	[InlineData("")]
	[InlineData(null)]
	public void NotHttp_NeverReachesTheRouter_AndTheFallbackRefuses(string? url)
	{
		_router.TakesUrls = true;

		Assert.False(_launcher.TryOpen(url));

		Assert.Empty(_router.Urls);
		Assert.Empty(_fallback.Opened);
	}
}

/// <summary>A router that records what it is offered and takes or refuses it as a test says.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakePreviewRouter : IPreviewRouter
{
	public bool TakesUrls { get; set; }

	public bool TakesFiles { get; set; }

	public List<PreviewItemModel> Urls { get; } = [];

	public List<(PreviewItemModel Item, PreviewTarget Target)> Files { get; } = [];

	public List<string?> Shown { get; } = [];

	public List<(string NodeId, string[] Ids)> ClosedFocus { get; } = [];

	public int Restores { get; private set; }

	public bool CanToggle => true;

	public bool IsEnabled => true;

	public bool IsActive => true;

	public event EventHandler? StateChanged
	{
		add { }
		remove { }
	}

	public string? CurrentNodeId { get; set; }

	public void SetEnabled(bool enabled)
	{
	}

	public bool WouldRouteFile(string? fileName) => TakesFiles;

	public bool TryOpenFile(PreviewItemModel item, PreviewTarget fileHandle, ITessynClient client)
	{
		Files.Add((item, fileHandle));
		return TakesFiles;
	}

	public bool TryOpenUrl(PreviewItemModel item)
	{
		Urls.Add(item);
		return TakesUrls;
	}

	public void OnSessionShown(string? nodeId) => Shown.Add(nodeId);

	public void OnFocusItemsClosed(string nodeId, IReadOnlyCollection<string> closedItemIds) => ClosedFocus.Add((nodeId, closedItemIds.ToArray()));

	public Task RestoreAsync(ITessynClient client)
	{
		Restores++;
		return Task.CompletedTask;
	}

	public void CloseAllForExit()
	{
	}
}
