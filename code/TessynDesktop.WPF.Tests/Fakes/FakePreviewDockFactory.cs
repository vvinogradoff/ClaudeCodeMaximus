using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Creates <see cref="FakePreviewWindowHandle"/>s for docked panels and remembers them in creation order; the default panel gets the id <c>Constants.Previews.DefaultPanelId</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakePreviewDockFactory : IPreviewDockFactory
{
	public List<FakePreviewWindowHandle> Created { get; } = [];

	public IEnumerable<FakePreviewWindowHandle> Open => Created.Where(h => !h.IsClosed);

	/// <summary>True: <see cref="Create"/> fails, as when the main window's docking area is not there.</summary>
	public bool ThrowOnCreate { get; set; }

	/// <summary>Called with the group of every panel being created (null = the default panel), before it exists.</summary>
	public Action<PreviewGroupModel?>? OnCreate { get; set; }

	public IPreviewWindowHandle Create(PreviewGroupModel? group, bool pinned, PreviewDockLayoutModel? ownLayout)
	{
		OnCreate?.Invoke(group);
		if (ThrowOnCreate)
			throw new InvalidOperationException("no docking area");

		var handle = new FakePreviewWindowHandle(group?.Id ?? Constants.Previews.DefaultPanelId, pinned, null, group?.Name ?? Constants.Previews.DefaultPanelName) { OwnLayout = ownLayout, CurrentLayout = ownLayout };
		Created.Add(handle);
		return handle;
	}
}
