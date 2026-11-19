using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Records the calls the docking test hook's handler makes and answers with a scripted result and a fixed state.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeDockingTestOperations : IDockingTestOperations
{
	public List<string> Calls { get; } = [];

	public DockingTestResult Result { get; set; } = DockingTestResult.Success;

	public Exception? Throw { get; set; }

	public List<DockingPanelState> Panels { get; } = [new DockingPanelState { Id = "sessions", Kind = "shell", Shown = true, Side = "left", Pane = 0, Selected = true, Width = 320 }];

	public IReadOnlyList<DockingPanelState> State() => Panels;

	public DockingTestResult Dock(string panel, string side) => Record($"dock {panel} {side}");

	public DockingTestResult Tab(string panel, string withPanel) => Record($"tab {panel} {withPanel}");

	public DockingTestResult Float(string panel, DockingFloatRect? rect) => Record(rect == null ? $"float {panel}" : $"float {panel} {rect.Left} {rect.Top} {rect.Width} {rect.Height}");

	public DockingTestResult AutoHide(string panel, bool on) => Record($"autohide {panel} {(on ? "on" : "off")}");

	public DockingTestResult SlideOut(string panel) => Record($"slideout {panel}");

	public DockingTestResult Close(string panel) => Record($"close {panel}");

	public DockingTestResult Show(string panel) => Record($"show {panel}");

	public DockingTestResult BringForward(string panel) => Record($"bringforward {panel}");

	public DockingTestResult Reset() => Record("reset");

	private DockingTestResult Record(string call)
	{
		Calls.Add(call);
		if (Throw != null)
			throw Throw;

		return Result;
	}
}
