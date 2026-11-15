using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>Asks <c>tree.nodeInfo</c> for paths and git origins in batches; a failure (an older daemon, a refusal) is logged and answers nothing, so the tree never breaks on it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeNodeInfoLoader
{
	private readonly ITessynClient _client;
	private readonly ILogger<TreeNodeInfoLoader> _logger;

	public TreeNodeInfoLoader(ITessynClient client, ILogger<TreeNodeInfoLoader> logger)
	{
		_client = client;
		_logger = logger;
	}

	public async Task<IReadOnlyList<TreeNodeInfoItem>> LoadAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken = default)
	{
		var items = new List<TreeNodeInfoItem>();
		for (var start = 0; start < nodeIds.Count; start += Constants.TreeLimits.NodeInfoBatch)
		{
			var batch = nodeIds.Skip(start).Take(Constants.TreeLimits.NodeInfoBatch).ToList();
			try
			{
				items.AddRange((await _client.TreeNodeInfoAsync(batch, cancellationToken)).Items);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "tree.nodeInfo failed for {Count} nodes", batch.Count);
				break;
			}
		}

		return items;
	}
}
