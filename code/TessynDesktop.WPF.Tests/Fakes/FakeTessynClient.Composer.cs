using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>The package J2 part of <see cref="FakeTessynClient"/>: turn options, drafts and autocomplete. Records its calls like the rest.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FakeTessynClient
{
	/// <summary>One <c>turn.send</c> as the fake received it.</summary>
	public sealed record TurnSendCall(string NodeId, string Text, string? ModelId, string? Effort, string? ProfileId, string? ClientRequestId, bool NewBranch, bool AutoCompact);

	/// <summary>Every <c>turn.send</c>, in order.</summary>
	public List<TurnSendCall> TurnSends { get; } = [];

	/// <summary>What <see cref="TurnSendAsync"/> answers.</summary>
	public TurnSendResult TurnSendAnswer { get; set; } = new("turn-1", Accepted: true, Queued: false);

	/// <summary>When set, <see cref="TurnSendAsync"/> throws it.</summary>
	public Exception? TurnSendException { get; set; }

	/// <summary>The drafts of the fake daemon, by session.</summary>
	public Dictionary<string, string> DraftTexts { get; } = [];

	/// <summary>When set, <see cref="DraftSetAsync"/> throws it (<see cref="DraftChangedConflict"/> is the daemon's answer to a stale revision).</summary>
	public Exception? DraftSetException { get; set; }

	/// <summary>What <see cref="AutocompleteSuggestAsync"/> answers.</summary>
	public AutocompleteSuggestResult AutocompleteAnswer { get; set; } = new(RemoteProtocol.Composer.AutocompleteModes.None, 0, 0, true, []);

	/// <summary>The error the daemon answers a <c>draft.set</c> with when its <c>baseRevision</c> is stale.</summary>
	public static TessynRpcException DraftChangedConflict() => new(RemoteProtocol.ErrorCodes.DaemonError, "Draft changed");

	/// <summary>The revision the fake reports for a draft text: stable for equal text, <see cref="RemoteProtocol.Composer.EmptyDraftRevision"/> for none.</summary>
	public static string RevisionOf(string? text) =>
		string.IsNullOrEmpty(text) ? RemoteProtocol.Composer.EmptyDraftRevision : "rev-" + text.Length + "-" + text.GetHashCode();

	public Task<TurnSendResult> TurnSendAsync(string nodeId, string text, string? modelId = null, string? effort = null, string? profileId = null, string? clientRequestId = null, bool newBranch = false, bool autoCompact = false, CancellationToken cancellationToken = default)
	{
		Record($"turn.send:{nodeId}");
		TurnSends.Add(new TurnSendCall(nodeId, text, modelId, effort, profileId, clientRequestId, newBranch, autoCompact));
		if (TurnSendException != null)
			throw TurnSendException;
		return Task.FromResult(TurnSendAnswer);
	}

	public Task<DraftGetResult> DraftGetAsync(string nodeId, CancellationToken cancellationToken = default)
	{
		Record($"draft.get:{nodeId}");
		DraftTexts.TryGetValue(nodeId, out var text);
		return Task.FromResult(new DraftGetResult(text ?? string.Empty, RevisionOf(text)));
	}

	public Task<DraftSetResult> DraftSetAsync(string nodeId, string text, string? baseRevision = null, CancellationToken cancellationToken = default)
	{
		Record($"draft.set:{nodeId}:{baseRevision ?? "-"}");
		if (DraftSetException != null)
			throw DraftSetException;

		DraftTexts.TryGetValue(nodeId, out var current);
		if (baseRevision != null && baseRevision != RevisionOf(current))
			throw DraftChangedConflict();

		if (text.Length == 0)
			DraftTexts.Remove(nodeId);
		else
			DraftTexts[nodeId] = text;
		return Task.FromResult(new DraftSetResult(RevisionOf(text)));
	}

	public Task<DraftDeleteResult> DraftDeleteAsync(string nodeId, CancellationToken cancellationToken = default)
	{
		Record($"draft.delete:{nodeId}");
		return Task.FromResult(new DraftDeleteResult(DraftTexts.Remove(nodeId)));
	}

	public Task<AutocompleteSuggestResult> AutocompleteSuggestAsync(string nodeId, string text, int caret, CancellationToken cancellationToken = default)
	{
		Record($"autocomplete.suggest:{nodeId}:{caret}");
		return Task.FromResult(AutocompleteAnswer);
	}

	/// <summary>The channels last registered with <see cref="ClientRegisterAsync"/> (empty: unregistered).</summary>
	public IReadOnlyList<string> RegisteredPresents { get; private set; } = [];

	public Task<ClientRegisterResult> ClientRegisterAsync(IReadOnlyList<string> presents, CancellationToken cancellationToken = default)
	{
		Record("client.register:" + string.Join(",", presents));
		RegisteredPresents = [.. presents];
		return Task.FromResult(new ClientRegisterResult(true));
	}

	/// <summary>Delivers a <c>notification.raised</c> as the daemon would publish it.</summary>
	public void RaiseNotification(string title, string message, string? nodeId = null, string notificationId = "notification-1") =>
		RaiseNotification(
			RemoteProtocol.Notifications.RaisedEvent,
			new NotificationRaisedPayload(notificationId, nodeId, title, message, RemoteProtocol.Notifications.UserNotificationKind, DateTimeOffset.UtcNow));
}
