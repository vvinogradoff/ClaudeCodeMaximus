using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Runs posted work inline, so view-model tests are deterministic.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
	public bool CheckAccess() => true;

	public void Post(Action action) => action();
}
