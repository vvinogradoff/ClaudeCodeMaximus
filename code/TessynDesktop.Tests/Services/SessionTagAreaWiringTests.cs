namespace TessynDesktop.Tests.Services;

/// <summary>
/// Regression guard for the Avalonia client's mini/full tag area (FR.24.16). The control's rules are unit tested (<see cref="TagAreaGestureInterpreterTests"/>, the layout calculator), but the
/// Avalonia client has no UI tests, so nothing would notice a header rework that swaps the <c>SessionTagArea</c> in <c>SessionView.axaml</c> for a plain item list again or drops the reset on a
/// session switch. This reads the two source files and fails when the control is missing from the session view or its wiring is gone.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagAreaWiringTests
{
	private static string Source(string relative)
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TessynDesktop.sln")))
			dir = dir.Parent;
		Assert.NotNull(dir);
		return File.ReadAllText(Path.Combine(dir.FullName, relative));
	}

	[Fact]
	public void TheSessionView_HostsTheTagArea_BoundToTheSessionsTagViewModel()
	{
		var view = Source(@"TessynDesktop\Views\SessionView.axaml");

		Assert.Matches(@"<views:SessionTagArea\s+DataContext=""\{Binding TagsVm\}""", view);
		// The area is shown only while the session has tags (the Panel carries it: a binding on the area itself resolves against its own DataContext).
		Assert.Matches(@"<Panel\s+IsVisible=""\{Binding TagsVm\.HasTags\}""[^>]*>\s*<views:SessionTagArea", view);
	}

	[Fact]
	public void TheSessionView_HasNoSecondPlainListOfTheTagChips()
	{
		var view = Source(@"TessynDesktop\Views\SessionView.axaml");

		// The chips are drawn by the area's own list; any other ItemsControl on TagsVm.Chips would be the plain list the area replaced.
		Assert.DoesNotMatch(@"ItemsSource=""\{Binding TagsVm\.Chips\}""", view);
	}

	[Fact]
	public void TheAreaControl_ExpandsAndCollapsesThroughTheGestureInterpreter_AndTheDocumentedModes()
	{
		var area = Source(@"TessynDesktop\Views\SessionTagArea.axaml.cs");

		Assert.Contains("TagAreaGestureAction.Expand", area);
		Assert.Contains("TagAreaGestureAction.Collapse", area);
		Assert.Contains("HiddenHintFormat", area);   // the "+N" hint of the collapsed line
		Assert.Contains("PointerPressedEvent", area);
		Assert.Contains("Scroller.Height", area);   // the mini line is a fixed height
	}

	[Fact]
	public void OpeningAnotherSession_ResetsTheAreaToOneLine()
	{
		var main = Source(@"TessynDesktop\ViewModels\MainWindowViewModel.cs");

		Assert.Matches(@"if \(!ReferenceEquals\(ActiveSession, vm\)\)\s*vm\.TagsVm\.ResetMode\(\);", main);
	}
}
