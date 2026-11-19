using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Views;

/// <summary>What is remembered of one floating shell panel (FR.6.18): its rectangle in the docking library's units, and the same place in physical pixels with its monitor and scale (FR.29.16).</summary>
/// <param name="LibraryUnits">What the library's <c>Floating*</c> values are set to.</param>
/// <param name="Pixels">The place with the device name of the monitor and its scale.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ShellFloatingEntry(WindowPlacementModel LibraryUnits, PreviewPlacementModel Pixels);
