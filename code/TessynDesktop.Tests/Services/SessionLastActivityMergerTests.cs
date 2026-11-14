using System;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class SessionLastActivityMergerTests
{
	private static readonly DateTimeOffset T1 = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset T2 = T1.AddMinutes(5);
	private readonly SessionLastActivityMerger _sut = new();

	[Fact]
	public void Merge_LiveValuesNewerThanScan_AreKept_IncludingProfile()
	{
		var live = new SessionLastActivityModel("f", T2, T2, "Live", T2);
		var scanned = new SessionLastActivityModel("f", T1, T1, "Old", T1);

		Assert.Equal(live, _sut.Merge(live, scanned));
	}

	[Fact]
	public void Merge_NullScan_NeverOverwritesGoodValues()
	{
		var live = new SessionLastActivityModel("f", T1, T1, "Live", T1);
		var scanned = new SessionLastActivityModel("f", null, null, null, null);

		Assert.Equal(live, _sut.Merge(live, scanned));
	}

	[Fact]
	public void Merge_NewerScan_OrMissingLive_TakesScannedValues()
	{
		var live = new SessionLastActivityModel("f", null, T1, "Stale", null);
		var scanned = new SessionLastActivityModel("f", T1, T2, "Scanned", T2);

		Assert.Equal(new SessionLastActivityModel("f", T1, T2, "Scanned", T2), _sut.Merge(live, scanned));
	}

	[Fact]
	public void Merge_ProfileFollowsPromptTimestamp_NotResponse()
	{
		var live = new SessionLastActivityModel("f", T2, null, "Live", null);
		var scanned = new SessionLastActivityModel("f", T1, T2, "Old", null);

		var merged = _sut.Merge(live, scanned);

		Assert.Equal(T2, merged.LastPromptTimestamp);
		Assert.Equal("Live", merged.ProfileName);
		Assert.Equal(T2, merged.LastResponseTimestamp);
	}
}
