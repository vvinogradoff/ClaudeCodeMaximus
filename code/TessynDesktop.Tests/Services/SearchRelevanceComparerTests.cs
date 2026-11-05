using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class SearchRelevanceComparerTests
{
	private static readonly SearchRelevanceComparer Comparer = new();
	private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

	private static SearchRelevanceKeyModel Key(int strict, int loose, DateTimeOffset? ts = null) => new(strict, loose, ts);

	[Fact]
	public void Match_SortsBeforeNull()
	{
		Assert.True(Comparer.Compare(Key(0, 1), null) < 0);
		Assert.True(Comparer.Compare(null, Key(0, 1)) > 0);
		Assert.Equal(0, Comparer.Compare(null, null));
	}

	[Fact]
	public void AnyStrictMatch_SortsBeforeLooseOnly_EvenWithHigherLooseCount()
	{
		Assert.True(Comparer.Compare(Key(1, 1), Key(0, 50)) < 0);
		Assert.True(Comparer.Compare(Key(0, 50), Key(1, 1)) > 0);
	}

	[Fact]
	public void HigherStrictCount_SortsFirst()
	{
		Assert.True(Comparer.Compare(Key(5, 5), Key(2, 9)) < 0);
	}

	[Fact]
	public void EqualStrict_HigherLooseCount_SortsFirst()
	{
		Assert.True(Comparer.Compare(Key(2, 9), Key(2, 3)) < 0);
		Assert.True(Comparer.Compare(Key(0, 4), Key(0, 2)) < 0);
	}

	[Fact]
	public void EqualCounts_MoreRecentPrompt_SortsFirst()
	{
		Assert.True(Comparer.Compare(Key(1, 2, Now), Key(1, 2, Now.AddDays(-1))) < 0);
	}

	[Fact]
	public void EqualCounts_NullTimestamp_SortsLast()
	{
		Assert.True(Comparer.Compare(Key(1, 2, Now), Key(1, 2)) < 0);
		Assert.True(Comparer.Compare(Key(1, 2), Key(1, 2, Now)) > 0);
		Assert.Equal(0, Comparer.Compare(Key(1, 2), Key(1, 2)));
	}

	[Fact]
	public void Sort_ProducesFullOrdering()
	{
		var keys = new List<SearchRelevanceKeyModel?>
		{
			null, Key(0, 1), Key(0, 3), Key(2, 2), Key(1, 4, Now), Key(1, 4, Now.AddDays(-2)), Key(1, 4),
		};

		var sorted = keys.Order(Comparer).ToList();

		Assert.Equal(
			new SearchRelevanceKeyModel?[] { Key(2, 2), Key(1, 4, Now), Key(1, 4, Now.AddDays(-2)), Key(1, 4), Key(0, 3), Key(0, 1), null },
			sorted);
	}
}
