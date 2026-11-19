using TessynDesktop.Client;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>A preview window that records what it was asked to do; tests raise its events themselves.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakePreviewWindowHandle : IDockedPreviewPanelHandle
{
	public FakePreviewWindowHandle(string groupId, bool pinned, PreviewPlacementModel? ownPlacement, string groupName)
	{
		GroupId = groupId;
		IsPinned = pinned;
		OwnPlacement = ownPlacement;
		CurrentPlacement = ownPlacement;
		GroupName = groupName;
	}

	public string GroupId { get; }

	public bool IsPinned { get; private set; }

	public PreviewItemModel? Item { get; set; }

	public PreviewPlacementModel? CurrentPlacement { get; set; }

	public PreviewPlacementModel? OwnPlacement { get; }

	public string GroupName { get; private set; }

	/// <summary>The layout a (pinned) panel was created with (FR.29.22).</summary>
	public PreviewDockLayoutModel? OwnLayout { get; init; }

	/// <summary>What <see cref="CurrentLayout"/> answers: the panel's layout now (a test sets it).</summary>
	public PreviewDockLayoutModel? CurrentLayout { get; set; }

	/// <summary>The panel's content id (a test sets it; the dock factory assigns one).</summary>
	public string? PanelId { get; set; }

	public List<PreviewItemModel> Shown { get; } = [];

	public PreviewTarget? LastTarget { get; private set; }

	public int CloseCalls { get; private set; }

	public bool IsClosed { get; private set; }

	public void Show(PreviewItemModel item, PreviewTarget? fileHandle, ITessynClient? client)
	{
		Item = item;
		LastTarget = fileHandle;
		Shown.Add(item);
	}

	public void SetGroupName(string name) => GroupName = name;

	/// <summary>Runs while the handle closes, before <see cref="Closed"/> (a real panel flushes its layout there).</summary>
	public Action? OnClosing { get; set; }

	public void Close()
	{
		CloseCalls++;
		OnClosing?.Invoke();
		RaiseClosed(byUser: false);
	}

	public event EventHandler<PreviewWindowClosedEventArgs>? Closed;

	public event EventHandler? Pinned;

	public event EventHandler? ItemChanged;

	public event EventHandler? PlacementChanged;

	/// <summary>The (pinned) panel settled at a new layout.</summary>
	public void ChangeLayout(PreviewDockLayoutModel layout)
	{
		CurrentLayout = layout;
		PlacementChanged?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>The (pinned) window settled at a new place.</summary>
	public void MoveTo(PreviewPlacementModel placement)
	{
		CurrentPlacement = placement;
		PlacementChanged?.Invoke(this, EventArgs.Empty);
	}

	public void RaiseClosed(bool byUser)
	{
		if (IsClosed)
			return;

		IsClosed = true;
		Closed?.Invoke(this, new PreviewWindowClosedEventArgs(byUser));
	}

	/// <summary>The user pins the window.</summary>
	public void Pin()
	{
		IsPinned = true;
		PanelId = GroupId + "#pinned" + Guid.NewGuid().ToString("N");   // as the real panel: a pinned panel gets an id of its own
		Pinned?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>The page inside the window navigated.</summary>
	public void Navigate(string url)
	{
		Item!.Url = url;
		ItemChanged?.Invoke(this, EventArgs.Empty);
	}
}
