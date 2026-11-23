using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IUserMessageChannels" />
/// <remarks>Created by Claude. Moved here from <c>AgentMcpServer.NotifyChannelsAsync</c> unchanged, so that the elevated-run request uses the very same fan-out.</remarks>
public sealed class UserMessageChannels(ITelegramBridgeService telegramBridge, INotificationService notifications, IDirectoryLabelService labelService) : IUserMessageChannels
{
	private static readonly ILogger _log = Log.ForContext<UserMessageChannels>();

	/// <inheritdoc />
	public async Task<IReadOnlyList<string>> NotifyAsync(SessionNodeModel node, string message)
	{
		var channels = new List<string>();
		try
		{
			if (await telegramBridge.NotifyAsync(node.NodeId, message))
				channels.Add(Constants.Attention.ChannelTelegram);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "User notification: Telegram channel failed for node {NodeId}", node.NodeId);
		}

		try
		{
			var title = string.Format(Constants.Attention.NotifyToastTitleFormat, labelService.GetLabel(node.WorkingDirectory), node.Name);
			// The toast service may wait up to 3 s for a presenting client's acknowledgement (V3). This method can still be running on the
			// accept loop's thread when every earlier await completed synchronously, so the blocking call is moved to the pool: one hung client
			// must not delay the other MCP and TDCP connections (R1).
			var nodeId = node.NodeId;
			if (await Task.Run(() => notifications.ShowUserNotification(nodeId, title, message)).ConfigureAwait(false))
				channels.Add(Constants.Attention.ChannelDesktop);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "User notification: desktop channel failed for node {NodeId}", node.NodeId);
		}

		return channels;
	}
}
