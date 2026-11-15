using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The unsent draft of a session for a local client (package J2): <c>draft.get</c>, <c>draft.set</c> (optimistic, with <c>baseRevision</c>) and
/// <c>draft.delete</c>. Loopback only. The change notification (<c>draft.changed</c>) is published by <see cref="RemoteNotificationPublisher"/>
/// from the <see cref="IDraftService"/> events, so a write of the Avalonia composer reaches the clients like a write of a client does.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DraftRpcModule : IRemoteRpcModule
{
	private readonly ISessionRegistry _registry;
	private readonly IDraftService _drafts;
	private readonly DraftRevisions _revisions = new();

	public DraftRpcModule(ISessionRegistry registry, IDraftService drafts)
	{
		_registry = registry;
		_drafts   = drafts;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync(RemoteProtocol.Composer.DraftGetMethod, Get, remoteCallable: false);
		registry.AddSync(RemoteProtocol.Composer.DraftSetMethod, Set, remoteCallable: false);
		registry.AddSync(RemoteProtocol.Composer.DraftDeleteMethod, Delete, remoteCallable: false);
	}

	private object? Get(JsonNode? @params)
	{
		var node = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));
		var text = _drafts.LoadDraft(node.FileName) ?? string.Empty;
		return new DraftGetResult(text, _revisions.Of(text));
	}

	private object? Set(JsonNode? @params)
	{
		var node         = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));
		var text         = TdcpParams.RequireString(@params, "text");
		var baseRevision = TdcpParams.OptionalString(@params, "baseRevision");

		// The compare and the write are one step inside the draft service (V6), so a save of the desktop composer cannot slip in between.
		// An empty draft is no draft (the Avalonia composer deletes it too): an empty text deletes.
		var written = _drafts.TryWriteDraft(node.FileName, text, current => baseRevision == null || baseRevision == _revisions.Of(current));
		if (!written)
			throw new TdcpException(Constants.Composer.DraftChangedMessage);

		return new DraftSetResult(_revisions.Of(text));
	}

	private object? Delete(JsonNode? @params)
	{
		var node = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));

		var existed = false;
		_drafts.TryWriteDraft(node.FileName, string.Empty, current =>
		{
			existed = current != null;
			return true;
		});
		return new DraftDeleteResult(existed);
	}
}
