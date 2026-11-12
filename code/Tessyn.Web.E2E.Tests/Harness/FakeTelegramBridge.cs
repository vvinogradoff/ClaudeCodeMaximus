using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>Telegram is off in the E2E harness.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeTelegramBridge : ITelegramBridgeService
{
	public void Start()
	{
	}

	public void Stop()
	{
	}

	public Task EnsureTopicForSessionAsync(SessionNodeModel node) => Task.CompletedTask;

	public Task<bool> NotifyAsync(string nodeId, string message) => Task.FromResult(false);
}
