namespace TessynDesktop.Models.Remote;

public static partial class RemoteProtocol
{
	/// <summary>
	/// Tree and node mutation (P1.J1), loopback only (<c>remoteCallable: false</c>). Every method is additive; each group is announced by its
	/// own capability flag of <c>client.hello</c>. A refused call is a JSON-RPC error whose message is the reason (never a path).
	/// </summary>
	public static class TreeMutation
	{
		/// <summary>The daemon serves <c>tree.addDirectory</c>, <c>tree.addGroup</c>, <c>tree.renameNode</c>, <c>tree.deleteNode</c>, <c>tree.moveNode</c> and <c>tree.moveTargets</c>.</summary>
		public const string Capability = "tree.mutate";

		/// <summary>The daemon serves <c>tree.nodeInfo</c> and <c>tree.refreshGitOrigins</c>.</summary>
		public const string InfoCapability = "tree.info";

		/// <summary><c>tree.addDirectory { path }</c> answers <c>{ directoryId, label }</c>. A path already in the tree answers its existing node.</summary>
		public const string AddDirectoryMethod = "tree.addDirectory";

		/// <summary><c>tree.addGroup { parentId, name }</c> answers <c>{ groupId }</c>; <c>parentId</c> is a directory or a group.</summary>
		public const string AddGroupMethod = "tree.addGroup";

		/// <summary><c>tree.renameNode { nodeId, name }</c> answers <c>{ name }</c> for a session or a group; a directory cannot be renamed.</summary>
		public const string RenameNodeMethod = "tree.renameNode";

		/// <summary><c>tree.deleteNode { nodeId, force? }</c> answers <c>{ deleted, blockedBy? }</c>; <c>force</c> also deletes the transcript file of a session.</summary>
		public const string DeleteNodeMethod = "tree.deleteNode";

		/// <summary>
		/// <c>tree.moveNode { nodeId, parentId, index? }</c> answers <c>{ moved }</c>. <c>index</c> counts the target's top-level sessions (workers are not
		/// counted, as in <c>tree.snapshot</c>) once the session has left its old place; omitted = first. Enforces the git-origin rule.
		/// </summary>
		public const string MoveNodeMethod = "tree.moveNode";

		/// <summary><c>tree.moveTargets { nodeId }</c> answers <c>{ parentIds[] }</c>: where the session may move under the git-origin rule.</summary>
		public const string MoveTargetsMethod = "tree.moveTargets";

		/// <summary><c>tree.nodeInfo { nodeIds[] }</c> (at most 500) answers <c>{ items[] }</c> with paths and git origins.</summary>
		public const string NodeInfoMethod = "tree.nodeInfo";

		/// <summary><c>tree.refreshGitOrigins {}</c> answers <c>{ started }</c>; <c>tree.changed</c> follows when the scan is done.</summary>
		public const string RefreshGitOriginsMethod = "tree.refreshGitOrigins";

		/// <summary>The reasons of <c>tree.deleteNode</c> answering <c>deleted: false</c>.</summary>
		public static class BlockedBy
		{
			/// <summary>The session has a transcript file; repeat with <c>force: true</c> to delete it too.</summary>
			public const string SessionFile = "sessionFile";

			/// <summary>The session has worker sessions; repeat with <c>force: true</c>.</summary>
			public const string Workers = "workers";

			/// <summary>A turn is running in the session. <c>force</c> does not override this.</summary>
			public const string Running = "running";

			/// <summary>A directory or group still holds sessions or groups. <c>force</c> does not override this.</summary>
			public const string NotEmpty = "notEmpty";
		}

		/// <summary>The values of <see cref="TreeNodeInfoItem.Kind"/>.</summary>
		public static class NodeKinds
		{
			public const string Directory = "directory";
			public const string Group = "group";
			public const string Session = "session";
		}
	}

	/// <summary>Session options and conversation reset (P1.J1), loopback only.</summary>
	public static class SessionOptions
	{
		/// <summary>The daemon serves <c>session.setOptions</c> and <c>session.resetConversation</c>.</summary>
		public const string Capability = "session.options";

		/// <summary>
		/// <c>session.setOptions { nodeId, autoCommit?, autoDocument?, playCompletionSound? }</c> answers <c>{ options, corrections? }</c>. A key that is
		/// absent is left alone; <c>autoCommit: null</c> is "No preference". Changing Auto-Commit or Auto-Document while the session runs sends the FR.11.12 correction.
		/// </summary>
		public const string SetMethod = "session.setOptions";

		/// <summary><c>session.resetConversation { nodeId }</c> answers <c>{ cleared }</c>: detaches the Claude session id (FR.11.8). Refused while a turn runs.</summary>
		public const string ResetMethod = "session.resetConversation";
	}

	/// <summary>The Local Tool Profile of a directory (P1.J1, FR.12.21), loopback only.</summary>
	public static class LocalTools
	{
		/// <summary>The daemon serves <c>localTools.get</c> and <c>localTools.set</c>.</summary>
		public const string Capability = "localtools";

		/// <summary><c>localTools.get { directoryId }</c> answers the profile and the selectable tools.</summary>
		public const string GetMethod = "localTools.get";

		/// <summary><c>localTools.set { directoryId, enabled, nativeTools[], includeCmxMcp, includedMcpServerIds[] }</c> stores the profile and answers it like get.</summary>
		public const string SetMethod = "localTools.set";
	}
}
