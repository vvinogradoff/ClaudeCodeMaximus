using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Tests;

/// <summary>The client-state file round trip (%AppData%\TessynDesktop\clients\wpf.json).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClientStateStoreTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-tests-" + Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		if (Directory.Exists(_root))
			Directory.Delete(_root, recursive: true);
	}

	private JsonClientStateStore Create(int debounceMs = 20) =>
		new(new AppPaths(_root), NullLogger<JsonClientStateStore>.Instance, TimeSpan.FromMilliseconds(debounceMs));

	[Fact]
	public async Task MissingFile_YieldsDefaults()
	{
		var store = Create();

		await store.LoadAsync();

		Assert.Null(store.State.Window);
		Assert.Null(store.State.LastSelectedNodeId);
		Assert.Null(store.State.ExpandedNodeIds);
		Assert.Equal(Constants.Limits.DefaultTreeWidth, store.State.TreeWidth);
	}

	[Fact]
	public async Task State_RoundTripsThroughTheFile()
	{
		var first = Create();
		await first.LoadAsync();
		first.Update(s =>
		{
			s.Window = new WindowPlacementModel { Left = 10, Top = 20, Width = 1200, Height = 700, Maximized = true };
			s.TreeWidth = 410.5;
			s.LastSelectedNodeId = "node-7";
			s.ExpandedNodeIds = ["d1", "g2", "s3"];
		});
		await first.FlushAsync();

		var second = Create();
		await second.LoadAsync();

		Assert.True(File.Exists(new AppPaths(_root).ClientStateFilePath));
		Assert.Equal(10, second.State.Window!.Left);
		Assert.Equal(20, second.State.Window.Top);
		Assert.Equal(1200, second.State.Window.Width);
		Assert.Equal(700, second.State.Window.Height);
		Assert.True(second.State.Window.Maximized);
		Assert.Equal(410.5, second.State.TreeWidth);
		Assert.Equal("node-7", second.State.LastSelectedNodeId);
		Assert.Equal(["d1", "g2", "s3"], second.State.ExpandedNodeIds);
	}

	[Fact]
	public async Task CorruptFile_FallsBackToDefaultsAndIsReplacedOnNextSave()
	{
		var path = new AppPaths(_root).ClientStateFilePath;
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		await File.WriteAllTextAsync(path, "{ not json");
		var store = Create();

		await store.LoadAsync();
		Assert.Null(store.State.LastSelectedNodeId);
		store.Update(s => s.LastSelectedNodeId = "x");
		await store.FlushAsync();

		var reloaded = Create();
		await reloaded.LoadAsync();
		Assert.Equal("x", reloaded.State.LastSelectedNodeId);
	}

	[Fact]
	public async Task ManyUpdates_AreCoalescedIntoTheLatestState()
	{
		var store = Create(debounceMs: 60);
		await store.LoadAsync();

		for (var i = 0; i < 50; i++)
		{
			var id = "n" + i;
			store.Update(s => s.LastSelectedNodeId = id);
		}

		var path = new AppPaths(_root).ClientStateFilePath;
		Assert.False(File.Exists(path));
		await store.FlushAsync();

		var reloaded = Create();
		await reloaded.LoadAsync();
		Assert.Equal("n49", reloaded.State.LastSelectedNodeId);
		Assert.False(File.Exists(path + ".tmp"));
	}

	[Fact]
	public async Task DebouncedWrite_HappensWithoutAnExplicitFlush()
	{
		var store = Create(debounceMs: 20);
		await store.LoadAsync();

		store.Update(s => s.LastSelectedNodeId = "later");
		var path = new AppPaths(_root).ClientStateFilePath;
		for (var i = 0; i < 100 && !File.Exists(path); i++)
			await Task.Delay(20);

		Assert.True(File.Exists(path));
	}

	[Fact]
	public async Task UnknownFieldsInTheFile_AreIgnored()
	{
		var path = new AppPaths(_root).ClientStateFilePath;
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		await File.WriteAllTextAsync(path, "{\"version\":1,\"futureThing\":{\"a\":1},\"lastSelectedNodeId\":\"k\"}");
		var store = Create();

		await store.LoadAsync();

		Assert.Equal("k", store.State.LastSelectedNodeId);
	}
}
