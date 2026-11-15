using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Records the files "opened in the default app", applying the real <see cref="ExternalOpenPolicy"/> like the shell launcher does.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeExternalFileLauncher : IExternalFileLauncher
{
	private readonly ExternalOpenPolicy _policy = new();

	public List<string> Opened { get; } = [];

	public bool TryOpen(string path)
	{
		if (!_policy.IsAllowed(path))
			return false;

		Opened.Add(path);
		return true;
	}
}
