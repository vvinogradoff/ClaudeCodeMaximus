using TessynDesktop.WPF.Transcript;

namespace TessynDesktop.WPF.Tests;

/// <summary>The LRU of loaded transcripts and the contiguous-run model.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TranscriptCacheTests
{
	private static TranscriptModel Model(string id) => new(id, 1, 0, [], []);

	[Fact]
	public void Put_BeyondCapacity_EvictsTheLeastRecentlyUsed()
	{
		var cache = new TranscriptCache(4);

		foreach (var id in new[] { "a", "b", "c", "d", "e" })
			cache.Put(Model(id));

		Assert.Equal(4, cache.Count);
		Assert.Equal(["e", "d", "c", "b"], cache.NodeIds);
		Assert.False(cache.TryGet("a", out _));
	}

	[Fact]
	public void TryGet_MarksTheEntryMostRecentlyUsed()
	{
		var cache = new TranscriptCache(3);
		cache.Put(Model("a"));
		cache.Put(Model("b"));
		cache.Put(Model("c"));

		Assert.True(cache.TryGet("a", out var model));
		cache.Put(Model("d"));

		Assert.Equal("a", model.NodeId);
		Assert.Equal(["d", "a", "c"], cache.NodeIds);
	}

	[Fact]
	public void Put_SameIdTwice_ReplacesInsteadOfDuplicating()
	{
		var cache = new TranscriptCache(4);
		cache.Put(Model("a"));
		cache.Put(Model("a"));

		Assert.Equal(1, cache.Count);
	}

	[Fact]
	public void RemoveAndClear_EmptyTheCache()
	{
		var cache = new TranscriptCache(4);
		cache.Put(Model("a"));
		cache.Put(Model("b"));

		cache.Remove("a");
		Assert.Equal(["b"], cache.NodeIds);
		cache.Clear();

		Assert.Equal(0, cache.Count);
	}

	[Fact]
	public void Memory_StaysBoundedAcrossTwoHundredSwitches()
	{
		var cache = new TranscriptCache(4);

		for (var i = 0; i < 200; i++)
			cache.Put(Model("s" + (i % 17)));

		Assert.Equal(4, cache.Count);
	}
}
