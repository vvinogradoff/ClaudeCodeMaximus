using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Records the URLs sent to the system browser (FR.26.9), applying the real <see cref="UrlPolicy"/>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeSystemUrlLauncher : ISystemUrlLauncher
{
	private readonly UrlPolicy _policy = new();

	public List<string> Opened { get; } = [];

	public bool TryOpen(string? url)
	{
		if (!_policy.IsAllowed(url))
			return false;

		Opened.Add(url!);
		return true;
	}
}
