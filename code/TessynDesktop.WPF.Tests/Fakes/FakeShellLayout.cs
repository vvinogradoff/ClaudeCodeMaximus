using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>An <see cref="IShellLayout"/> with panels that are open or closed and auto-hide that toggles; records what was asked.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeShellLayout : IShellLayout
{
	private readonly HashSet<string> _open = [Constants.DockedShell.SessionsPanelId, Constants.DockedShell.RecentPanelId, Constants.DockedShell.TagsPanelId, Constants.DockedShell.FocusPanelId];
	private readonly HashSet<string> _autoHidden = [];

	public List<string> BroughtForward { get; } = [];

	public int Resets { get; private set; }

	public event EventHandler? Changed;

	public bool IsOpen(string panelId) => _open.Contains(panelId);

	public void BringForward(string panelId)
	{
		BroughtForward.Add(panelId);
		_open.Add(panelId);
		Changed?.Invoke(this, EventArgs.Empty);
	}

	public void Close(string panelId)
	{
		_open.Remove(panelId);
		Changed?.Invoke(this, EventArgs.Empty);
	}

	public bool CanToggleAutoHide(string panelId) => _open.Contains(panelId);

	public bool IsAutoHidden(string panelId) => _autoHidden.Contains(panelId);

	public void ToggleAutoHide(string panelId)
	{
		if (!CanToggleAutoHide(panelId))
			return;

		if (!_autoHidden.Remove(panelId))
			_autoHidden.Add(panelId);

		Changed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>What <see cref="IsSelectedWithSessionView"/> answers; tests set it to the layout they want.</summary>
	public bool FocusSelectedWithSession { get; set; }

	public bool IsSelectedWithSessionView(string panelId) => FocusSelectedWithSession && panelId == Constants.DockedShell.FocusPanelId;

	public void ResetLayout() => Resets++;
}
