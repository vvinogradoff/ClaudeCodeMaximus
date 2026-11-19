using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// <see cref="IPreviewSettingsService"/> over the client settings store. It listens to the store's <c>Changed</c> event for its two sections, so a write from
/// anywhere (not only through this service) raises the matching event.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewSettingsService : IPreviewSettingsService
{
	private readonly IClientSettingsStore _store;
	private bool _lastEnabled;

	public PreviewSettingsService(IClientSettingsStore store)
	{
		_store = store;
		_lastEnabled = store.Get<PreviewWindowsSettings>().Enabled;
		_store.Changed += OnStoreChanged;
	}

	public event EventHandler? GroupsChanged;

	public event EventHandler? WindowsEnabledChanged;

	public IReadOnlyList<PreviewGroupModel> Groups
	{
		get
		{
			var stored = _store.Get<PreviewSettings>().Groups;
			if (stored == null)
				return Constants.Previews.DefaultGroups.Select(d => new PreviewGroupModel { Id = d.Id, Name = d.Name, Patterns = d.Patterns }).ToList();

			return stored.Select(g => g.Clone()).ToList();
		}
	}

	public bool WindowsEnabled => _store.Get<PreviewWindowsSettings>().Enabled;

	public void SaveGroups(IReadOnlyList<PreviewGroupModel> groups)
	{
		var kept = groups.Select(g => g.Clone()).ToList();
		_store.Update<PreviewSettings>(s => s.Groups = kept);
		ForgetPlacementsOutside(kept.Select(g => g.Id));
	}

	public void ResetGroups()
	{
		_store.Update<PreviewSettings>(s => s.Groups = null);
		ForgetPlacementsOutside(Groups.Select(g => g.Id));
	}

	/// <summary>FR.29.2: the remembered placement of a group that no longer exists goes with it.</summary>
	private void ForgetPlacementsOutside(IEnumerable<string> groupIds)
	{
		var ids = groupIds.ToHashSet(StringComparer.Ordinal);
		if (!_store.Get<PreviewWindowsSettings>().Placements.Keys.All(ids.Contains))
		{
			_store.Update<PreviewWindowsSettings>(s =>
			{
				foreach (var gone in s.Placements.Keys.Where(k => !ids.Contains(k)).ToList())
					s.Placements.Remove(gone);
			});
		}

		// The default Preview panel is not a group and keeps its layout.
		ids.Add(Constants.Previews.DefaultPanelId);
		if (_store.Get<PreviewDockSettings>().Layouts.Keys.All(ids.Contains))
			return;

		_store.Update<PreviewDockSettings>(s =>
		{
			foreach (var gone in s.Layouts.Keys.Where(k => !ids.Contains(k)).ToList())
				s.Layouts.Remove(gone);
		});
	}

	public void SetAutoClose(string groupId, bool autoClose)
	{
		if (!Groups.Any(g => g.Id == groupId))
			return;

		// Start from the current list (the defaults when never edited) so flipping one switch also stores the rest.
		var groups = Groups.ToList();
		groups.First(g => g.Id == groupId).AutoClose = autoClose;
		SaveGroups(groups);
	}

	public void SetWindowsEnabled(bool enabled) => _store.Update<PreviewWindowsSettings>(s => s.Enabled = enabled);

	public PreviewPlacementModel? GetPlacement(string groupId) =>
		_store.Get<PreviewWindowsSettings>().Placements.TryGetValue(groupId, out var placement) ? placement : null;

	/// <summary>Stores the placement of an existing group; the id of a group that no longer exists is ignored, so no orphan placement is left behind (FR.29.2).</summary>
	public void SavePlacement(string groupId, PreviewPlacementModel placement)
	{
		if (!Groups.Any(g => g.Id == groupId))
			return;

		_store.Update<PreviewWindowsSettings>(s => s.Placements[groupId] = placement);
	}

	public void ForgetPlacements()
	{
		_store.Update<PreviewWindowsSettings>(s => s.Placements.Clear());
		_store.Update<PreviewDockSettings>(s => s.Layouts.Clear());
	}

	public PreviewDockLayoutModel? GetDockLayout(string groupId) =>
		_store.Get<PreviewDockSettings>().Layouts.TryGetValue(groupId, out var layout) ? layout.Clone() : null;

	public void SaveDockLayout(string groupId, PreviewDockLayoutModel layout)
	{
		var copy = layout.Clone();
		_store.Update<PreviewDockSettings>(s => s.Layouts[groupId] = copy);
	}

	public IReadOnlyList<OpenPreviewPanelModel> OpenPanels => _store.Get<PreviewDockSettings>().OpenPanels.Select(CloneOpenPanel).ToList();

	public void SaveOpenPanels(IReadOnlyList<OpenPreviewPanelModel> panels)
	{
		var copies = panels.Select(CloneOpenPanel).ToList();
		_store.Update<PreviewDockSettings>(s => s.OpenPanels = copies);
	}

	private OpenPreviewPanelModel CloneOpenPanel(OpenPreviewPanelModel p) => new()
	{
		GroupId = p.GroupId,
		Pinned = p.Pinned,
		Item = p.Item.Clone(),
		Layout = p.Layout?.Clone(),
	};

	public IReadOnlyList<OpenPreviewWindowModel> OpenWindows => _store.Get<PreviewWindowsSettings>().OpenWindows;

	public void SaveOpenWindows(IReadOnlyList<OpenPreviewWindowModel> windows) =>
		_store.Update<PreviewWindowsSettings>(s => s.OpenWindows = windows.ToList());

	private void OnStoreChanged(object? sender, ClientSettingsChangedEventArgs e)
	{
		if (e.Section == Constants.SettingsSections.Previews)
		{
			GroupsChanged?.Invoke(this, EventArgs.Empty);
			return;
		}

		if (e.Section != Constants.SettingsSections.PreviewWindows)
			return;

		// The section also changes when a placement is saved; only a changed toggle is an event.
		var enabled = WindowsEnabled;
		if (enabled == _lastEnabled)
			return;

		_lastEnabled = enabled;
		WindowsEnabledChanged?.Invoke(this, EventArgs.Empty);
	}
}
