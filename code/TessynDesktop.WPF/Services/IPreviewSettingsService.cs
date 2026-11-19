using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The single source of truth for the preview groups (section <c>previews</c>) and the preview windows' runtime state (section <c>previewWindows</c>).
/// The events are raised on the thread that wrote the section, which may not be the UI thread.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IPreviewSettingsService
{
	/// <summary>Copies of the groups; the defaults when the list was never edited.</summary>
	IReadOnlyList<PreviewGroupModel> Groups { get; }

	/// <summary>After any write of the previews section (Settings save, auto-close flip, reset).</summary>
	event EventHandler? GroupsChanged;

	/// <summary>Settings → Save; the caller validates the groups with <see cref="PreviewGroupMatcher"/>.</summary>
	void SaveGroups(IReadOnlyList<PreviewGroupModel> groups);

	/// <summary>Back to the default groups.</summary>
	void ResetGroups();

	/// <summary>A preview window's Auto-close button; an unknown id does nothing.</summary>
	void SetAutoClose(string groupId, bool autoClose);

	/// <summary>The stored toggle choice (not the effective mode, which also needs two monitors).</summary>
	bool WindowsEnabled { get; }

	event EventHandler? WindowsEnabledChanged;

	void SetWindowsEnabled(bool enabled);

	/// <summary>The remembered placement of the group's window; null when none.</summary>
	PreviewPlacementModel? GetPlacement(string groupId);

	void SavePlacement(string groupId, PreviewPlacementModel placement);

	/// <summary>Settings → Forget window positions.</summary>
	void ForgetPlacements();

	/// <summary>The saved list of open preview windows (copies), for reopening them at start (FR.29.11).</summary>
	IReadOnlyList<OpenPreviewWindowModel> OpenWindows { get; }

	/// <summary>Replaces the saved list of open preview windows.</summary>
	void SaveOpenWindows(IReadOnlyList<OpenPreviewWindowModel> windows);

	/// <summary>The remembered layout of a group's docked panel (or of the default Preview panel); null when none (FR.29.21).</summary>
	PreviewDockLayoutModel? GetDockLayout(string groupId);

	/// <summary>Remembers a group's docked panel layout.</summary>
	void SaveDockLayout(string groupId, PreviewDockLayoutModel layout);

	/// <summary>The saved list of open docked panels (copies), for reopening them at start (FR.29.22).</summary>
	IReadOnlyList<OpenPreviewPanelModel> OpenPanels { get; }

	/// <summary>Replaces the saved list of open docked panels.</summary>
	void SaveOpenPanels(IReadOnlyList<OpenPreviewPanelModel> panels);
}
