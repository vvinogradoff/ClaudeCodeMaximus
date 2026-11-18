using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>An <see cref="IAppearanceService"/> that counts the subscribers of <see cref="Changed"/> and can raise it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CountingAppearanceService : IAppearanceService
{
	public ClientTheme Chosen => ClientTheme.Light;

	public bool IsDark => false;

	public AppearanceSettings Current { get; } = new();

	public IReadOnlyDictionary<string, string> PageColours { get; } = new Dictionary<string, string>();

	public int Subscribers => Changed?.GetInvocationList().Length ?? 0;

	public event EventHandler? Changed;

	public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

	public void ToggleTheme()
	{
	}

	public void AdjustFont(FontTarget target, int delta)
	{
	}
}
