using System.Runtime.InteropServices;
using System.Windows;

namespace TessynDesktop.WPF.Infrastructure;

/// <summary>
/// The width of the sizing frame of a window as Windows draws it for a given DPI. A maximised window with a custom frame reaches over the edge of its monitor by this
/// much, and <see cref="SystemParameters.WindowResizeBorderThickness"/> only knows the DPI of the primary monitor, so on a second monitor with another scale
/// the content would hang over the edge or leave a gap.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class FrameMetrics
{
	private const int SmCxSizeFrame = 32;
	private const int SmCySizeFrame = 33;
	private const int SmCxPaddedBorder = 92;

	[DllImport("user32.dll")]
	private static extern int GetSystemMetricsForDpi(int index, uint dpi);

	/// <summary>The frame thickness in device pixels at <paramref name="dpi"/>: the sizing frame plus the padded border, per side.</summary>
	public static (int X, int Y) FramePixels(uint dpi)
	{
		var padded = GetSystemMetricsForDpi(SmCxPaddedBorder, dpi);
		return (GetSystemMetricsForDpi(SmCxSizeFrame, dpi) + padded, GetSystemMetricsForDpi(SmCySizeFrame, dpi) + padded);
	}

	/// <summary>The margin that pulls the content of a maximised window back inside its monitor, in DIPs, for a monitor whose scale is <paramref name="scale"/> (1.0 = 96 dpi).</summary>
	public static Thickness MaximizedMargin(double scale)
	{
		var (x, y) = FramePixels((uint)Math.Round(96 * scale));
		return FromPixels(x, y, scale);
	}

	/// <summary>Converts a frame in device pixels to DIPs.</summary>
	public static Thickness FromPixels(int x, int y, double scale) => new(x / scale, y / scale, x / scale, y / scale);
}
