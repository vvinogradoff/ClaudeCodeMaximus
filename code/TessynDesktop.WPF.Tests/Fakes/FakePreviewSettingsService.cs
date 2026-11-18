using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>An in-memory <see cref="IPreviewSettingsService"/>: records the writes and raises <see cref="GroupsChanged"/> on demand.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakePreviewSettingsService : IPreviewSettingsService
{
	private readonly Dictionary<string, PreviewPlacementModel> _placements = [];
	private List<PreviewGroupModel> _groups = [];

	public FakePreviewSettingsService(params PreviewGroupModel[] groups)
	{
		_groups = groups.ToList();
	}

	public List<(string GroupId, bool AutoClose)> AutoCloseWrites { get; } = [];

	public List<(string GroupId, PreviewPlacementModel Placement)> PlacementWrites { get; } = [];

	public IReadOnlyList<PreviewGroupModel> Groups => _groups.Select(g => g.Clone()).ToList();

	public bool WindowsEnabled { get; private set; } = true;

	public IReadOnlyList<OpenPreviewWindowModel> OpenWindows { get; private set; } = [];

	public int GroupsChangedSubscribers => GroupsChanged?.GetInvocationList().Length ?? 0;

	public event EventHandler? GroupsChanged;

	public event EventHandler? WindowsEnabledChanged
	{
		add { }
		remove { }
	}

	public void SaveGroups(IReadOnlyList<PreviewGroupModel> groups)
	{
		_groups = groups.Select(g => g.Clone()).ToList();
		GroupsChanged?.Invoke(this, EventArgs.Empty);
	}

	public void ResetGroups() => GroupsChanged?.Invoke(this, EventArgs.Empty);

	public void SetAutoClose(string groupId, bool autoClose)
	{
		AutoCloseWrites.Add((groupId, autoClose));
		var group = _groups.FirstOrDefault(g => g.Id == groupId);
		if (group == null)
			return;

		group.AutoClose = autoClose;
		GroupsChanged?.Invoke(this, EventArgs.Empty);
	}

	public void SetWindowsEnabled(bool enabled) => WindowsEnabled = enabled;

	public PreviewPlacementModel? GetPlacement(string groupId) => _placements.GetValueOrDefault(groupId);

	public void SavePlacement(string groupId, PreviewPlacementModel placement)
	{
		_placements[groupId] = placement;
		PlacementWrites.Add((groupId, placement));
	}

	public void ForgetPlacements() => _placements.Clear();

	public void SaveOpenWindows(IReadOnlyList<OpenPreviewWindowModel> windows) => OpenWindows = windows;
}
