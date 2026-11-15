namespace TessynDesktop.Models.Remote;

public static partial class RemoteProtocol
{
	/// <summary>
	/// Turn and composer extras of package J2: the per-run toggles of <c>turn.send</c>, the session's auth error, the unsent drafts and the
	/// autocomplete. All methods are loopback only (<c>remoteCallable: false</c>); <c>draft.changed</c> is a local-stream-only notification.
	/// Each feature is announced by its own capability flag of <c>client.hello</c>.
	/// </summary>
	public static class Composer
	{
		/// <summary><c>turn.send</c> accepts <c>newBranch</c> and <c>autoCompact</c>; the engine runs Auto-Compact after the answer (also with an empty text).</summary>
		public const string TurnOptionsCapability = "turn.options";

		/// <summary><see cref="SessionStateDto.AuthError"/> is published with the session state (<c>session.get</c>, <c>tree.snapshot</c>, <c>session.changed</c>).</summary>
		public const string AuthErrorCapability = "session.authError";

		/// <summary><c>draft.get</c>, <c>draft.set</c>, <c>draft.delete</c> and the <c>draft.changed</c> notification.</summary>
		public const string DraftsCapability = "drafts";

		/// <summary><c>autocomplete.suggest</c>.</summary>
		public const string AutocompleteCapability = "autocomplete";

		/// <summary><c>draft.get { nodeId, itemId? }</c> answers <c>{ text, revision }</c>. With <c>itemId</c> (FR.23.19) it is the reply draft of that Focus item; without it, the session's composer draft. The same holds for <c>draft.set</c> and <c>draft.delete</c>.</summary>
		public const string DraftGetMethod = "draft.get";

		/// <summary><c>draft.set { nodeId, text, baseRevision? }</c> answers <c>{ revision }</c>; an empty text deletes the draft; a stale <c>baseRevision</c> is error -32000 "Draft changed".</summary>
		public const string DraftSetMethod = "draft.set";

		/// <summary><c>draft.delete { nodeId }</c> answers <c>{ deleted }</c>.</summary>
		public const string DraftDeleteMethod = "draft.delete";

		/// <summary>Notification <c>{ nodeId, revision, itemId? }</c> (<c>itemId</c> only for the reply draft of a Focus item): the draft changed (any writer, this client included). Local stream only, coalesced.</summary>
		public const string DraftChangedEvent = "draft.changed";

		/// <summary><c>autocomplete.suggest { nodeId, text, caret }</c> answers <c>{ mode, triggerStart, triggerLength, indexReady, items[] }</c>.</summary>
		public const string AutocompleteSuggestMethod = "autocomplete.suggest";

		/// <summary>Revision of a session that has no draft; also the <c>baseRevision</c> for the first write.</summary>
		public const string EmptyDraftRevision = "0";

		/// <summary>Mode names of an autocomplete answer: <c>none</c>, <c>symbol</c> (<c>#</c>), <c>file</c> (<c>##</c>) and <c>path</c> (a drive path).</summary>
		public static class AutocompleteModes
		{
			public const string None = "none";
			public const string Symbol = "symbol";
			public const string File = "file";
			public const string Path = "path";
		}

		/// <summary>The notifications that only the local stream carries (the relay filters them out).</summary>
		public static readonly IReadOnlyList<string> LocalOnlyEvents = [DraftChangedEvent];
	}
}
