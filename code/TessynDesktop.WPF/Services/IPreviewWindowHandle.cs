using TessynDesktop.Client;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>One preview window as the router sees it (FR.29.5, FR.29.7, FR.29.8); the WPF window behind it lives in the view layer. Called on the UI thread.</summary>
/// <remarks>Created by Claude</remarks>
public interface IPreviewWindowHandle
{
	string GroupId { get; }

	/// <summary>The user pinned the window (one-way, FR.29.8).</summary>
	bool IsPinned { get; }

	/// <summary>A copy of the item shown now (a web page's address is kept current); null before the first <see cref="Show"/>.</summary>
	PreviewItemModel? Item { get; }

	/// <summary>The window's placement now, for saving a pinned window's own place at exit (FR.29.11); null when it cannot be read.</summary>
	PreviewPlacementModel? CurrentPlacement { get; }

	/// <summary>
	/// Shows <paramref name="item"/>, replacing what the window showed. The first call opens the window without activating it; later calls restore it when
	/// minimized and bring it to the top without taking the keyboard focus. A file needs <paramref name="fileHandle"/> (a <c>refId</c> or a <c>file.preview</c>
	/// handle) and <paramref name="client"/> to read it; a web page needs neither.
	/// </summary>
	void Show(PreviewItemModel item, PreviewTarget? fileHandle, ITessynClient? client);

	/// <summary>The group was renamed in Settings: the title follows.</summary>
	void SetGroupName(string name);

	/// <summary>Closes the window on the router's decision; raises <see cref="Closed"/> with <c>ByUser</c> false.</summary>
	void Close();

	/// <summary>The window closed (by the user or by <see cref="Close"/>).</summary>
	event EventHandler<PreviewWindowClosedEventArgs>? Closed;

	/// <summary>The user pinned the window.</summary>
	event EventHandler? Pinned;

	/// <summary>The item changed inside the window (a web page navigated), so the saved list must be written again.</summary>
	event EventHandler? ItemChanged;

	/// <summary>A pinned window was moved, resized or changed state and stayed so; the saved list must be written again (FR.29.11). Not raised by a group window, which saves its own placement.</summary>
	event EventHandler? PlacementChanged;
}
