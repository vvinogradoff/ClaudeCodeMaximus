using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.29.6, FR.29.8, FR.29.11, FR.29.13: when a preview window's place is written, and where. A fake clock stands in for the 500 ms wait.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewPlacementTrackerTests
{
	private const string GroupId = "images";

	private readonly FakePreviewSettingsService _settings = new(new PreviewGroupModel { Id = GroupId, Name = "Images", Patterns = "png" });
	private readonly FakeMonitorWatcher _monitors = new(2);
	private readonly ManualTimeProvider _time = new();
	private readonly PreviewPlacementModel _placement = new() { Left = 10, Top = 20, Width = 800, Height = 600 };
	private bool _pinned;
	private bool _minimized;
	private int _pinnedEvents;

	private PreviewPlacementTracker NewTracker(bool testWindow = false, bool tracking = true)
	{
		var tracker = new PreviewPlacementTracker(
			GroupId, _settings, _monitors, new ImmediateUiDispatcher(), () => _pinned, () => _minimized, () => _placement, testWindow, _time) { Tracking = tracking };
		tracker.PinnedPlacementChanged += (_, _) => _pinnedEvents++;
		return tracker;
	}

	private static readonly TimeSpan Delay = Constants.PreviewWindow.PlacementSaveDelay + TimeSpan.FromMilliseconds(1);

	[Fact]
	public void AGroupWindow_SavesItsPlace_AfterTheDelay_NotBefore()
	{
		using var tracker = NewTracker();

		tracker.Changed();
		_time.Advance(TimeSpan.FromMilliseconds(400));
		Assert.Null(_settings.GetPlacement(GroupId));

		_time.Advance(TimeSpan.FromMilliseconds(200));

		Assert.Equal(800, _settings.GetPlacement(GroupId)!.Width);
		Assert.Equal(0, _pinnedEvents);
	}

	[Fact]
	public void EveryChange_RestartsTheWait()
	{
		using var tracker = NewTracker();

		tracker.Changed();
		_time.Advance(TimeSpan.FromMilliseconds(400));
		tracker.Changed();
		_time.Advance(TimeSpan.FromMilliseconds(400));
		Assert.Null(_settings.GetPlacement(GroupId));

		_time.Advance(TimeSpan.FromMilliseconds(200));
		Assert.NotNull(_settings.GetPlacement(GroupId));
	}

	[Fact]
	public void APinnedWindow_NeverWritesTheGroupsPlace_ButTellsTheRouter()
	{
		_pinned = true;
		using var tracker = NewTracker();

		tracker.Changed();
		_time.Advance(Delay);

		Assert.Null(_settings.GetPlacement(GroupId));
		Assert.Equal(1, _pinnedEvents);
	}

	[Fact]
	public void AWindowPinnedWhileTheChangeWaited_WritesNothingForTheGroup()
	{
		using var tracker = NewTracker();
		tracker.Changed();
		_pinned = true;   // the user pinned it within the 500 ms

		_time.Advance(Delay);

		Assert.Null(_settings.GetPlacement(GroupId));
		Assert.Equal(1, _pinnedEvents);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void WhileMinimized_NothingIsSaved_ForAGroupOrAPinnedWindow(bool pinned)
	{
		_pinned = pinned;
		_minimized = true;
		using var tracker = NewTracker();

		tracker.Changed();
		_time.Advance(Delay);

		Assert.Null(_settings.GetPlacement(GroupId));
		Assert.Equal(0, _pinnedEvents);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void WhileWindowsSettlesAfterADisplayChange_NothingIsSaved(bool pinned)
	{
		_pinned = pinned;
		_monitors.Settling = true;
		using var tracker = NewTracker();

		tracker.Changed();
		_time.Advance(Delay);

		Assert.Null(_settings.GetPlacement(GroupId));
		Assert.Equal(0, _pinnedEvents);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void InTestMode_NothingIsSaved(bool pinned)
	{
		_pinned = pinned;
		using var tracker = NewTracker(testWindow: true);

		tracker.Changed();
		_time.Advance(Delay);
		tracker.Flush();

		Assert.Null(_settings.GetPlacement(GroupId));
		Assert.Equal(0, _pinnedEvents);
	}

	[Fact]
	public void BeforeTheFirstFrame_TheWindowBeingPlacedIsNotAUserMove()
	{
		using var tracker = NewTracker(tracking: false);

		tracker.Changed();
		_time.Advance(Delay);

		Assert.Null(_settings.GetPlacement(GroupId));
	}

	[Fact]
	public void Flush_SavesAChangeStillWaiting_OnceOnly()
	{
		using var tracker = NewTracker();
		tracker.Changed();

		tracker.Flush();
		Assert.NotNull(_settings.GetPlacement(GroupId));

		_settings.ForgetPlacements();
		tracker.Flush();   // nothing is waiting anymore
		_time.Advance(Delay);
		Assert.Null(_settings.GetPlacement(GroupId));
	}

	[Fact]
	public void ADisposedTracker_DoesNothing()
	{
		var tracker = NewTracker();
		tracker.Changed();

		tracker.Dispose();
		_time.Advance(Delay);
		tracker.Changed();

		Assert.Null(_settings.GetPlacement(GroupId));
	}
}
