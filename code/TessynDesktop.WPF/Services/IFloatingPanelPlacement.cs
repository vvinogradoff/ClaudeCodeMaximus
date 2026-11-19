using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The placement rules of a floating docked panel's window (FR.29.16, FR.29.21, FR.6.18) over the Win32 calls and the monitors present, so that the docking code stays testable with a fake.
/// A floating panel is remembered like a preview window: its actual rectangle in physical pixels with the device name of its monitor and that monitor's scale.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IFloatingPanelPlacement
{
	/// <summary>True while Windows is rearranging windows after a display change: a move seen now is forced and is never remembered (FR.29.21).</summary>
	bool IsSettling { get; }

	/// <summary>The place to remember for the floating window <paramref name="hwnd"/>; null when it cannot be read.</summary>
	PreviewPlacementModel? Capture(IntPtr hwnd);

	/// <summary>
	/// The rectangle to give the docking library's <c>Floating*</c> values for a remembered place: sizes converted by the ratio of the scales, clamped to the monitors present; a place
	/// saved without a scale (older data) is only clamped. Device-independent units when the place carries a scale (it was remembered in pixels), as saved otherwise.
	/// </summary>
	WindowPlacementModel Plan(PreviewPlacementModel saved);

	/// <summary>A remembered pixel place in the units of the docking library's <c>Floating*</c> values (device-independent); as it is without a scale.</summary>
	WindowPlacementModel ToLibraryUnits(PreviewPlacementModel pixels);
}
