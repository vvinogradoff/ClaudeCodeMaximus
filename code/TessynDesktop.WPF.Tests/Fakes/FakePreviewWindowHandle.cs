using TessynDesktop.Client;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>A preview window that records what it was asked to do; tests raise its events themselves.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakePreviewWindowHandle : IPreviewWindowHandle
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

	public void Close()
	{
		CloseCalls++;
		RaiseClosed(byUser: false);
	}

	public event EventHandler<PreviewWindowClosedEventArgs>? Closed;

	public event EventHandler? Pinned;

	public event EventHandler? ItemChanged;

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
		Pinned?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>The page inside the window navigated.</summary>
	public void Navigate(string url)
	{
		Item!.Url = url;
		ItemChanged?.Invoke(this, EventArgs.Empty);
	}
}
