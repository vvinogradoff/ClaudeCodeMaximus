using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>An <see cref="AppPaths"/> over a fresh temp folder that is deleted on dispose.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TempAppPaths : IDisposable
{
	public TempAppPaths()
	{
		Root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-test-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Root);
		Paths = new AppPaths(Root);
	}

	public string Root { get; }

	public AppPaths Paths { get; }

	public void Dispose()
	{
		try
		{
			Directory.Delete(Root, recursive: true);
		}
		catch (IOException)
		{
		}
	}
}
