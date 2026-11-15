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

	// Read-compare-write of draft.set must be one step, or two clients both pass the revision check.
	private readonly object _writeGate = new();

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

		lock (_writeGate)
		{
			if (baseRevision != null && baseRevision != _revisions.Of(_drafts.LoadDraft(node.FileName)))
				throw new TdcpException(Constants.Composer.DraftChangedMessage);

			// An empty draft is no draft (the Avalonia composer deletes it too).
			if (text.Length == 0)
				_drafts.DeleteDraft(node.FileName);
			else
				_drafts.SaveDraft(node.FileName, text);

			return new DraftSetResult(_revisions.Of(text));
		}
	}

	private object? Delete(JsonNode? @params)
	{
		var node = RemoteSessionLookup.Require(_registry, TdcpParams.RequireString(@params, "nodeId"));

		lock (_writeGate)
		{
			var existed = _drafts.LoadDraft(node.FileName) != null;
			_drafts.DeleteDraft(node.FileName);
			return new DraftDeleteResult(existed);
		}
	}
}
