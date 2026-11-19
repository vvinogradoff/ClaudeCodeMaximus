using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>A scriptable Win32 placement seam: the rectangle Windows keeps behind the state, the actual one (after a Snap they differ), the DPI, and every rectangle applied.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeWindowPlacementApi : IWindowPlacementApi
{
	public WindowPlacementModel Behind { get; set; } = new() { Left = 100, Top = 100, Width = 800, Height = 600 };

	public WindowPlacementModel Actual { get; set; } = new() { Left = 100, Top = 100, Width = 800, Height = 600 };

	public bool Restored { get; set; } = true;

	/// <summary>While not restored: maximized (true) or minimized (false).</summary>
	public bool Maximized { get; set; }

	public int Dpi { get; set; } = 96;

	public List<WindowPlacementModel> Applied { get; } = [];

	public WindowPlacementModel? Capture(IntPtr hwnd)
	{
		var behind = Behind.Clone();
		behind.Maximized = !Restored && Maximized;
		return behind;
	}

	public bool IsRestored(IntPtr hwnd) => Restored;

	public WindowPlacementModel? GetActualRectangle(IntPtr hwnd) => Actual.Clone();

	public int GetDpi(IntPtr hwnd) => Dpi;

	/// <summary>Runs inside <see cref="Apply"/>, where Windows raises WM_DPICHANGED.</summary>
	public Action? OnApply { get; set; }

	public void Apply(IntPtr hwnd, WindowPlacementModel model)
	{
		Applied.Add(model.Clone());
		OnApply?.Invoke();
	}
}
