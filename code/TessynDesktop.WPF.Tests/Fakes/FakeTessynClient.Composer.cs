using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>The package J2 part of <see cref="FakeTessynClient"/>: drafts and autocomplete (turn.send with its J2 flags lives in the main part). Records its calls like the rest.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FakeTessynClient
{
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

	/// <summary>When set, <see cref="DraftGetAsync"/> waits for it before it reads the draft (the user types while the read is pending).</summary>
	public TaskCompletionSource? DraftGetGate { get; set; }

	public async Task<DraftGetResult> DraftGetAsync(string nodeId, CancellationToken cancellationToken = default)
	{
		Record($"draft.get:{nodeId}");
		DraftTexts.TryGetValue(nodeId, out var text);   // read before the gate: a slow answer is an old one, as on a real connection
		if (DraftGetGate != null)
			await DraftGetGate.Task;

		return new DraftGetResult(text ?? string.Empty, RevisionOf(text));
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

	/// <summary>When set, answers <see cref="AutocompleteSuggestAsync"/> instead of <see cref="AutocompleteAnswer"/> (text and caret are passed; it may wait to model a slow daemon).</summary>
	public Func<string, int, Task<AutocompleteSuggestResult>>? AutocompleteHandler { get; set; }

	public Task<AutocompleteSuggestResult> AutocompleteSuggestAsync(string nodeId, string text, int caret, CancellationToken cancellationToken = default)
	{
		Record($"autocomplete.suggest:{nodeId}:{caret}");
		return AutocompleteHandler?.Invoke(text, caret) ?? Task.FromResult(AutocompleteAnswer);
	}

	/// <summary>The channels last registered with <see cref="ClientRegisterAsync"/> (empty: unregistered).</summary>
	public IReadOnlyList<string> RegisteredPresents { get; private set; } = [];

	public Task<ClientRegisterResult> ClientRegisterAsync(IReadOnlyList<string> presents, CancellationToken cancellationToken = default)
	{
		Record("client.register:" + string.Join(",", presents));
		RegisteredPresents = [.. presents];
		return Task.FromResult(new ClientRegisterResult(true));
	}

	/// <summary>The ids passed to <see cref="NotificationAckAsync"/>, in order.</summary>
	public List<string> AckedNotifications { get; } = [];

	/// <summary>The session ids passed to <see cref="NotificationSoundAckAsync"/>, in order.</summary>
	public List<string> AckedSounds { get; } = [];

	public Task<NotificationAckResult> NotificationAckAsync(string notificationId, CancellationToken cancellationToken = default)
	{
		Record("notification.ack:" + notificationId);
		AckedNotifications.Add(notificationId);
		return Task.FromResult(new NotificationAckResult(true));
	}

	public Task<NotificationAckResult> NotificationSoundAckAsync(string nodeId, CancellationToken cancellationToken = default)
	{
		Record("notification.ack.sound:" + nodeId);
		AckedSounds.Add(nodeId);
		return Task.FromResult(new NotificationAckResult(true));
	}

	/// <summary>Delivers a <c>notification.raised</c> as the daemon would publish it.</summary>
	public void RaiseNotification(string title, string message, string? nodeId = null, string notificationId = "notification-1") =>
		RaiseNotification(
			RemoteProtocol.Notifications.RaisedEvent,
			new NotificationRaisedPayload(notificationId, nodeId, title, message, RemoteProtocol.Notifications.UserNotificationKind, DateTimeOffset.UtcNow));
}
