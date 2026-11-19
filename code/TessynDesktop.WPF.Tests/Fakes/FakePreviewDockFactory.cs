using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Creates <see cref="FakePreviewWindowHandle"/>s for docked panels and remembers them in creation order; the default panel gets the id <c>Constants.Previews.DefaultPanelId</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakePreviewDockFactory : IPreviewDockFactory
{
	public List<FakePreviewWindowHandle> Created { get; } = [];

	public IEnumerable<FakePreviewWindowHandle> Open => Created.Where(h => !h.IsClosed);

	public IPreviewWindowHandle Create(PreviewGroupModel? group, bool pinned, PreviewDockLayoutModel? ownLayout)
	{
		var handle = new FakePreviewWindowHandle(group?.Id ?? Constants.Previews.DefaultPanelId, pinned, null, group?.Name ?? Constants.Previews.DefaultPanelName);
		Created.Add(handle);
		return handle;
	}
}
