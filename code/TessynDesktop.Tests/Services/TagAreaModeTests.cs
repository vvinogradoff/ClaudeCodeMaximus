using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>The tag area mode is per opened session, collapsed by default and never remembered (FR.24.16).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagAreaModeTests
{
	[Fact]
	public void DefaultIsCollapsed() => Assert.False(new TagAreaMode().IsExpanded);

	[Fact]
	public void Set_ChangesTheMode_AndRaisesChangedOnlyOnARealChange()
	{
		var mode = new TagAreaMode();
		var raised = 0;
		mode.Changed += () => raised++;

		mode.Set(true);
		mode.Set(true);
		Assert.True(mode.IsExpanded);
		Assert.Equal(1, raised);

		mode.Set(false);
		Assert.False(mode.IsExpanded);
		Assert.Equal(2, raised);
	}

	[Fact]
	public void Reset_CollapsesAnExpandedArea_AndDoesNothingWhenCollapsed()
	{
		var mode = new TagAreaMode();
		var raised = 0;
		mode.Changed += () => raised++;

		mode.Reset();
		Assert.Equal(0, raised);

		mode.Set(true);
		mode.Reset();
		Assert.False(mode.IsExpanded);
		Assert.Equal(2, raised);
	}

	[Fact]
	public void ModesOfTwoSessionsAreIndependent()
	{
		var a = new TagAreaMode();
		var b = new TagAreaMode();
		a.Set(true);
		Assert.False(b.IsExpanded);
	}
}
