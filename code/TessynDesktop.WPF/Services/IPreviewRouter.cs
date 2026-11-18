using TessynDesktop.Client;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Decides where a previewed item opens (FR.29.1, FR.29.3–FR.29.11): the matching group's window while the preview windows mode is on, otherwise the
/// caller opens it as before (preview pane, default player, system browser). Owns the group windows and the pinned windows, auto-close, the mode,
/// monitor changes and reopening after a restart. Contains no WPF types; called on the UI thread.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IPreviewRouter
{
	/// <summary>Two or more monitors are connected: the title-bar toggle is enabled (FR.29.1).</summary>
	bool CanToggle { get; }

	/// <summary>The stored toggle choice (default on).</summary>
	bool IsEnabled { get; }

	/// <summary>The preview windows mode: <see cref="CanToggle"/> and <see cref="IsEnabled"/>.</summary>
	bool IsActive { get; }

	/// <summary><see cref="CanToggle"/>, <see cref="IsEnabled"/> or <see cref="IsActive"/> changed; raised on the UI thread.</summary>
	event EventHandler? StateChanged;

	/// <summary>Stores the toggle choice; turning it off closes the group windows (pinned ones stay).</summary>
	void SetEnabled(bool enabled);

	/// <summary>The session the main window shows now: the source of items opened without a Focus item.</summary>
	string? CurrentNodeId { get; }

	/// <summary>True when a file of this name would go to a preview window now (mode on and a matching group); lets a caller decide before fetching anything (a video path).</summary>
	bool WouldRouteFile(string? fileName);

	/// <summary>Opens a file in its group's window; false when the mode is off or no group matches <see cref="PreviewItemModel.FileName"/>, and the caller opens it as before.</summary>
	bool TryOpenFile(PreviewItemModel item, PreviewTarget fileHandle, ITessynClient client);

	/// <summary>Opens an http(s) address (<see cref="PreviewItemModel.Url"/>) in its group's window; false when the mode is off or no group matches, and the caller opens it as before.</summary>
	bool TryOpenUrl(PreviewItemModel item);

	/// <summary>The main window shows <paramref name="nodeId"/> now (null: no session); applies auto-close rule 1 (FR.29.9).</summary>
	void OnSessionShown(string? nodeId);

	/// <summary>Focus items of <paramref name="nodeId"/> that are no longer open (answered, resolved, dismissed); applies auto-close rule 2 (FR.29.9).</summary>
	void OnFocusItemsClosed(string nodeId, IReadOnlyCollection<string> closedItemIds);

	/// <summary>Reopens the windows of the last run (FR.29.11), once per app run; later calls do nothing.</summary>
	Task RestoreAsync(ITessynClient client);

	/// <summary>The app closes: saves the open windows (pinned ones with their own placement) and closes them without forgetting them (FR.29.11).</summary>
	void CloseAllForExit();
}
