using TessynDesktop.Client;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>A scripted <see cref="IPreviewRouter"/> for the title bar and the main window: its state is set by the test and the calls are recorded.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakePreviewRouter : IPreviewRouter
{
	public bool CanToggle { get; private set; }

	public bool IsEnabled { get; private set; } = true;

	public bool IsActive => CanToggle && IsEnabled;

	public string? CurrentNodeId => null;

	public int ExitCalls { get; private set; }

	public List<bool> EnabledWrites { get; } = [];

	public event EventHandler? StateChanged;

	public void SetEnabled(bool enabled)
	{
		EnabledWrites.Add(enabled);
		IsEnabled = enabled;
		StateChanged?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>Sets what the monitors allow (two or more: true) and raises <see cref="StateChanged"/>.</summary>
	public void SetCanToggle(bool canToggle)
	{
		CanToggle = canToggle;
		StateChanged?.Invoke(this, EventArgs.Empty);
	}

	public bool WouldRouteFile(string? fileName) => false;

	public bool TryOpenFile(PreviewItemModel item, PreviewTarget fileHandle, ITessynClient client) => false;

	public bool TryOpenUrl(PreviewItemModel item) => false;

	public void OnSessionShown(string? nodeId)
	{
	}

	public void OnFocusItemsClosed(string nodeId, IReadOnlyCollection<string> closedItemIds)
	{
	}

	public Task RestoreAsync(ITessynClient client) => Task.CompletedTask;

	public void CloseAllForExit() => ExitCalls++;
}
