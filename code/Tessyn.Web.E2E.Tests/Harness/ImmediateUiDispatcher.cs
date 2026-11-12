using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>Runs UI work inline: there is no UI thread in the E2E harness.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
	public void Post(Action action) => action();

	public Task InvokeAsync(Action action)
	{
		action();
		return Task.CompletedTask;
	}

	public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
}
