using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// <see cref="IShellLayout"/> for services and view models, which exist before the main window: forwards to the main window's docking area once it registered, and
/// answers "nothing open, nothing to do" before (FR.6.14–FR.6.16). Raises <see cref="Changed"/> when the area does.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellLayoutHost : IShellLayout
{
	private IShellLayout? _layout;

	public event EventHandler? Changed;

	public void Register(IShellLayout layout)
	{
		_layout = layout;
		layout.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		Changed?.Invoke(this, EventArgs.Empty);
	}

	public bool IsOpen(string panelId) => _layout?.IsOpen(panelId) ?? false;

	public void BringForward(string panelId) => _layout?.BringForward(panelId);

	public void Close(string panelId) => _layout?.Close(panelId);

	public bool CanToggleAutoHide(string panelId) => _layout?.CanToggleAutoHide(panelId) ?? false;

	public bool IsAutoHidden(string panelId) => _layout?.IsAutoHidden(panelId) ?? false;

	public void ToggleAutoHide(string panelId) => _layout?.ToggleAutoHide(panelId);

	public bool IsSelectedWithSessionView(string panelId) => _layout?.IsSelectedWithSessionView(panelId) ?? false;

	public void ResetLayout() => _layout?.ResetLayout();
}
