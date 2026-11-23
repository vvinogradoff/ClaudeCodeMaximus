using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>The Telegram plus desktop fan-out of the daemon, recorded and never sent (the elevated-run request notifies through it, FR.23.31).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeUserMessageChannels : IUserMessageChannels
{
	public List<(string NodeId, string Message)> Sent { get; } = [];

	public Task<IReadOnlyList<string>> NotifyAsync(SessionNodeModel node, string message)
	{
		lock (Sent)
			Sent.Add((node.NodeId, message));
		return Task.FromResult<IReadOnlyList<string>>([]);
	}
}
