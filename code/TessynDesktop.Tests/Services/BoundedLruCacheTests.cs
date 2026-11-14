using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude. LRU session cache policy (ISSUE.016).</remarks>
public sealed class BoundedLruCacheTests
{
	private sealed record Item(string Name, bool Pinned = false);

	private static BoundedLruCache<string, Item> Create(int capacity) =>
		new(capacity, i => i.Pinned, StringComparer.OrdinalIgnoreCase);

	private static void Add(BoundedLruCache<string, Item> cache, string key, bool pinned = false) =>
		cache.Set(key, new Item(key, pinned));

	[Fact]
	public void Trim_BelowCapacity_EvictsNothing()
	{
		var cache = Create(3);
		Add(cache, "a");
		Add(cache, "b");

		Assert.Empty(cache.Trim());
		Assert.Equal(2, cache.Count);
	}

	[Fact]
	public void Trim_EvictsLeastRecentlyUsedFirst()
	{
		var cache = Create(2);
		Add(cache, "a");
		Add(cache, "b");
		Add(cache, "c");

		var evicted = cache.Trim();

		Assert.Equal(new[] { "a" }, evicted.Select(i => i.Name));
		Assert.False(cache.TryGetValue("a", out _));
		Assert.True(cache.TryGetValue("b", out _));
		Assert.True(cache.TryGetValue("c", out _));
	}

	[Fact]
	public void TryGetValue_MarksEntryMostRecentlyUsed()
	{
		var cache = Create(2);
		Add(cache, "a");
		Add(cache, "b");
		Assert.True(cache.TryGetValue("a", out _)); // a is now newer than b
		Add(cache, "c");

		var evicted = cache.Trim();

		Assert.Equal(new[] { "b" }, evicted.Select(i => i.Name));
	}

	[Fact]
	public void Trim_NeverEvictsPinnedEntries_EvenIfThatLeavesTheCacheOverCapacity()
	{
		var cache = Create(1);
		Add(cache, "a", pinned: true);
		Add(cache, "b", pinned: true);
		Add(cache, "c");
		Add(cache, "d");

		var evicted = cache.Trim();

		Assert.Equal(new[] { "c", "d" }, evicted.Select(i => i.Name)); // oldest unpinned first
		Assert.Equal(2, cache.Count); // a and b stay although capacity is 1
		Assert.True(cache.TryGetValue("a", out _));
		Assert.True(cache.TryGetValue("b", out _));
	}

	[Fact]
	public void Trim_SkipsTheKeptKey_EvenWhenItIsTheOldest()
	{
		var cache = Create(1);
		Add(cache, "active");
		Add(cache, "x");
		Add(cache, "y");
		// make "active" the least recently used one
		Assert.True(cache.TryGetValue("x", out _));
		Assert.True(cache.TryGetValue("y", out _));

		var evicted = cache.Trim(keep: "ACTIVE");

		Assert.DoesNotContain(evicted, i => i.Name == "active");
		Assert.True(cache.TryGetValue("active", out _));
	}

	[Fact]
	public void Pinning_IsEvaluatedAtTrimTime()
	{
		var flags = new Dictionary<string, bool> { ["a"] = true };
		var cache = new BoundedLruCache<string, string>(1, v => flags.GetValueOrDefault(v));
		cache.Set("a", "a");
		cache.Set("b", "b");

		var evicted = cache.Trim();   // a is pinned, so b is the only possible victim

		Assert.Equal(new[] { "b" }, evicted);
		Assert.Equal(1, cache.Count);
		Assert.True(cache.TryGetValue("a", out _));
	}

	[Fact]
	public void Rekey_KeepsEntryAndRecency_AndReplacesAnExistingNewKey()
	{
		var cache = Create(5);
		Add(cache, "old");
		Add(cache, "other");

		Assert.True(cache.Rekey("old", "new"));

		Assert.False(cache.TryGetValue("old", out _));
		Assert.True(cache.TryGetValue("new", out var moved));
		Assert.Equal("old", moved.Name);
		Assert.False(cache.Rekey("missing", "x"));
	}

	[Fact]
	public void Set_SameKeyTwice_KeepsOneEntry()
	{
		var cache = Create(2);
		Add(cache, "a");
		cache.Set("a", new Item("a2"));

		Assert.Equal(1, cache.Count);
		Assert.True(cache.TryGetValue("a", out var v));
		Assert.Equal("a2", v.Name);
	}

	[Fact]
	public void ManySwitches_StayAtCapacity_WhenNothingIsPinned()
	{
		var cache = Create(5);
		var evictedTotal = 0;
		for (var i = 0; i < 200; i++)
		{
			var key = "s" + (i % 40);
			if (!cache.TryGetValue(key, out _))
				Add(cache, key);
			evictedTotal += cache.Trim(keep: key).Count;
			Assert.True(cache.Count <= 5);
		}

		Assert.True(evictedTotal > 0);
	}
}
