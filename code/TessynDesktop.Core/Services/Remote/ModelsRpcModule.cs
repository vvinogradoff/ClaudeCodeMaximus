using System;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// <c>models.refresh</c> (package J3 part 2): asks the local Ollama instance for its models again and publishes <c>catalog.changed</c>. The daemon stops the
/// asking after <see cref="Constants.IntegrationsApi.ModelsRefreshTimeout"/>. Loopback only.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ModelsRpcModule : IRemoteRpcModule
{
	private readonly IClaudeModelService _models;
	private readonly IRemoteEventBus? _bus;
	private readonly TimeSpan _timeout;

	public ModelsRpcModule(IClaudeModelService models, IRemoteEventBus? bus = null, TimeSpan? timeout = null)
	{
		_models  = models;
		_bus     = bus;
		_timeout = timeout ?? Constants.IntegrationsApi.ModelsRefreshTimeout;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry) =>
		registry.Add(RemoteProtocol.ModelsApi.RefreshMethod, RefreshAsync, remoteCallable: false);

	private async Task<object?> RefreshAsync(System.Text.Json.Nodes.JsonNode? @params, CancellationToken cancellationToken)
	{
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		linked.CancelAfter(_timeout);
		var refreshing = _models.RefreshAsync(linked.Token);
		try
		{
			await refreshing.WaitAsync(_timeout, cancellationToken);
		}
		catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
		{
			linked.Cancel();
			_ = refreshing.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
			throw new TdcpException("Refreshing the models took too long");
		}

		_bus?.Publish(RemoteProtocol.ModelsApi.CatalogChangedEvent, new { });
		return new ModelsRefreshResult(_models.GetCachedModels().Count);
	}
}
