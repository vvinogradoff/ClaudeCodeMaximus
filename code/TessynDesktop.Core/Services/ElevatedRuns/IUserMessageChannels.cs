using System.Collections.Generic;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// The one Telegram plus desktop fan-out for a message to the user (FR.21.5, FR.23.9): shared by <c>notify_user</c>, <c>post_attention</c> and the request of an
/// elevated run (FR.23.31). Each channel is gated as today and a failure in one never stops the other.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IUserMessageChannels
{
	/// <summary>Sends <paramref name="message"/> for <paramref name="node"/>; returns the channels that delivered (<see cref="Constants.Attention.ChannelTelegram"/>, <see cref="Constants.Attention.ChannelDesktop"/>).</summary>
	Task<IReadOnlyList<string>> NotifyAsync(SessionNodeModel node, string message);
}
