using AvalonDock.Layout;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// What the shell layout remembers of its floating panels beyond the library's own rectangle (FR.6.18, FR.29.16): each one's place in pixels with the device name of its monitor and its
/// scale, read from the panel's window. A move Windows forces after a display change is never taken over (<see cref="FloatingPlacementGuard"/>); the last good place is what is saved.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellFloatingPlacements
{
	private readonly IFloatingPanelPlacement _placement;
	private readonly Func<LayoutAnchorable, IntPtr> _handleOf;
	private readonly Dictionary<string, FloatingPlacementGuard> _guards = new();
	private readonly Dictionary<string, ShellFloatingEntry> _entries = new();

	public ShellFloatingPlacements(IFloatingPanelPlacement placement, Func<LayoutAnchorable, IntPtr> handleOf)
	{
		_placement = placement;
		_handleOf = handleOf;
	}

	/// <summary>The place to save per floating panel id; a floating panel whose window cannot be read is not in it (the library's own rectangle is saved then).</summary>
	public IReadOnlyDictionary<string, ShellFloatingEntry> Entries => _entries;

	/// <summary>Reads the floating panels' windows now; a panel that no longer floats is forgotten.</summary>
	public void Observe(IReadOnlyCollection<LayoutAnchorable> floating)
	{
		var ids = floating.Select(p => p.ContentId).ToHashSet();
		foreach (var gone in _entries.Keys.Where(id => !ids.Contains(id)).ToList())
		{
			_entries.Remove(gone);
			_guards.Remove(gone);
		}

		var settling = _placement.IsSettling;
		foreach (var panel in floating)
		{
			var handle = _handleOf(panel);
			if (handle == IntPtr.Zero)
				continue;

			if (!_guards.TryGetValue(panel.ContentId, out var guard))
				_guards[panel.ContentId] = guard = new FloatingPlacementGuard();

			var pixels = guard.Filter(_placement.Capture(handle), settling);
			if (pixels != null)
				_entries[panel.ContentId] = new ShellFloatingEntry(_placement.ToLibraryUnits(pixels), pixels);
		}
	}

	/// <summary>A panel restored at <paramref name="saved"/>: the place counts as remembered until its window can be read.</summary>
	public void Seed(string panelId, PreviewPlacementModel saved)
	{
		_guards[panelId] = new FloatingPlacementGuard();
		_entries[panelId] = new ShellFloatingEntry(_placement.ToLibraryUnits(saved), saved);
	}

	/// <summary>The rectangle a remembered place is restored to (scale ratio, clamped to the monitors present).</summary>
	public WindowPlacementModel Plan(PreviewPlacementModel saved) => _placement.Plan(saved);
}
