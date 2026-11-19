using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>An <see cref="IShellLayout"/> that records the panels brought forward.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeShellLayout : IShellLayout
{
	public List<string> BroughtForward { get; } = [];

	public bool IsOpen(string panelId) => true;

	public void BringForward(string panelId) => BroughtForward.Add(panelId);

	public void Close(string panelId)
	{
	}

	public bool CanToggleAutoHide(string panelId) => false;

	public bool IsAutoHidden(string panelId) => false;

	public void ToggleAutoHide(string panelId)
	{
	}

	public void ResetLayout()
	{
	}

	public event EventHandler? Changed
	{
		add { }
		remove { }
	}
}
