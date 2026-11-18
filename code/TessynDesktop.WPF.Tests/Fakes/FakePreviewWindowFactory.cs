using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Creates <see cref="FakePreviewWindowHandle"/>s and remembers them in creation order.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakePreviewWindowFactory : IPreviewWindowFactory
{
	public List<FakePreviewWindowHandle> Created { get; } = [];

	public IEnumerable<FakePreviewWindowHandle> Open => Created.Where(h => !h.IsClosed);

	public IPreviewWindowHandle Create(PreviewGroupModel group, bool pinned, PreviewPlacementModel? ownPlacement)
	{
		var handle = new FakePreviewWindowHandle(group.Id, pinned, ownPlacement, group.Name);
		Created.Add(handle);
		return handle;
	}
}
