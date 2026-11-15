using System;
using System.Threading.Tasks;
using TessynDesktop.Services;
using TessynDesktop.Tests.Contract;
using TessynDesktop.Tests.Services.Turns;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>ISSUE.016: the autocomplete never waits for an index build and does not keep the indexes it asked for.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AutocompleteIndexLeasesTests : IDisposable
{
	private const string Directory = @"C:\proj";

	private readonly FakeCodeIndexService _index = new();
	private readonly ManualTime _time = new();
	private readonly AutocompleteIndexLeases _sut;

	public AutocompleteIndexLeasesTests() => _sut = new AutocompleteIndexLeases(_index, _time);

	public void Dispose() => _sut.Dispose();

	/// <summary>A clock the test moves.</summary>
	private sealed class ManualTime : TimeProvider
	{
		private DateTimeOffset _now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow() => _now;
		public void Advance(TimeSpan by) => _now += by;
	}

	[Fact]
	public async Task TheFirstRequest_StartsTheBuildInTheBackground_AndReturnsNotReadyAtOnce()
	{
		var ready = _sut.Touch(Directory);

		Assert.False(ready);
		await Waits.Until(() => _index.BuildStarts == 1);
	}

	[Fact]
	public async Task RequestsWhileItBuilds_DoNotStartASecondBuild()
	{
		_sut.Touch(Directory);
		await Waits.Until(() => _index.BuildStarts == 1);

		_sut.Touch(Directory);
		_sut.Touch(Directory);

		Assert.Equal(1, _index.BuildStarts);
	}

	[Fact]
	public async Task OnceBuilt_TheIndexIsReady_AndStaysHeldWhileRequestsKeepComing()
	{
		_sut.Touch(Directory);
		_index.FinishBuild();
		await Waits.Until(() => _index.Acquired == 1);

		_time.Advance(TimeSpan.FromMinutes(4));
		Assert.True(_sut.Touch(Directory));
		_time.Advance(TimeSpan.FromMinutes(4));
		_sut.ReleaseIdle();

		Assert.Equal(0, _index.Released);
	}

	[Fact]
	public async Task AnIdleLease_IsReleased_AndTheNextRequestBuildsAgain()
	{
		_sut.Touch(Directory);
		_index.FinishBuild();
		await Waits.Until(() => _index.Acquired == 1);

		_time.Advance(Constants.Composer.AutocompleteIndexIdle + TimeSpan.FromSeconds(1));
		_sut.ReleaseIdle();

		Assert.Equal(1, _index.Released);
		Assert.False(_sut.Touch(Directory));
		await Waits.Until(() => _index.BuildStarts == 2);
	}

	[Fact]
	public void AnIndexAnotherHolderHasReady_IsUsedWithoutALeaseOfOurs()
	{
		_index.SetReady();

		Assert.True(_sut.Touch(Directory));

		Assert.Equal(0, _index.BuildStarts);
		_time.Advance(TimeSpan.FromHours(1));
		_sut.ReleaseIdle();
		Assert.Equal(0, _index.Released);
	}

	[Fact]
	public async Task ABuildThatNeverReturns_IsGivenUpAfterTheTimeout_SoTheDirectoryIsNotNotReadyForever()
	{
		_sut.Touch(Directory);
		await Waits.Until(() => _index.BuildStarts == 1); // the fake build never finishes

		_time.Advance(Constants.Composer.AutocompleteBuildTimeout - TimeSpan.FromSeconds(1));
		_sut.ReleaseIdle();
		_sut.Touch(Directory);
		Assert.Equal(1, _index.BuildStarts); // still within the timeout: no second build

		_time.Advance(TimeSpan.FromSeconds(2));
		_sut.ReleaseIdle();
		Assert.False(_sut.Touch(Directory));

		await Waits.Until(() => _index.BuildStarts == 2);
	}

	[Fact]
	public async Task AGivenUpBuildThatFinishesLate_ReleasesTheReferenceItGot()
	{
		_sut.Touch(Directory);
		await Waits.Until(() => _index.BuildStarts == 1);
		_time.Advance(Constants.Composer.AutocompleteBuildTimeout + TimeSpan.FromSeconds(1));
		_sut.ReleaseIdle();

		_index.FinishBuild();

		await Waits.Until(() => _index.Acquired == 1 && _index.Released == 1);
	}

	[Fact]
	public async Task Dispose_GivesBackWhatWasAcquired()
	{
		_sut.Touch(Directory);
		_index.FinishBuild();
		await Waits.Until(() => _index.Acquired == 1);

		_sut.Dispose();

		Assert.Equal(1, _index.Released);
	}

	[Fact]
	public void ABlankDirectory_IsNeverReady_AndStartsNothing()
	{
		Assert.False(_sut.Touch(" "));
		Assert.Equal(0, _index.BuildStarts);
	}
}
