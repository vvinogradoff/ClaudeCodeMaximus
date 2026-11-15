namespace TessynDesktop.Models.Remote;

public static partial class RemoteProtocol
{
	/// <summary>
	/// Global search, session import, schedules, the attention list of all sessions and the daemon health (package J4). Every method is loopback
	/// only (<c>remoteCallable: false</c>), so the relay answers -32601. Each group is announced by its own capability flag of <c>client.hello</c>.
	/// A refusal is a JSON-RPC error whose message is the reason (never a path).
	/// </summary>
	public static class Search
	{
		/// <summary>The daemon serves <c>search.global</c> and <c>search.reindex</c>.</summary>
		public const string Capability = "search.global";

		/// <summary><c>search.global { query, projectSlug?, limit?, offset? }</c> answers <c>{ hits[], total }</c>: the sessions of every project whose messages match, one hit per session.</summary>
		public const string GlobalMethod = "search.global";

		/// <summary><c>search.reindex {}</c> answers <c>{ started }</c>; the scan runs in the background and <c>index.changed</c> follows.</summary>
		public const string ReindexMethod = "search.reindex";
	}

	/// <summary>Import of Claude Code sessions into the tree (FR.13).</summary>
	public static class Import
	{
		/// <summary>The daemon serves <c>import.list</c>, <c>import.titles</c> and <c>import.run</c>.</summary>
		public const string Capability = "import";

		/// <summary>
		/// <c>import.list { directoryId?, global?, query?, limit?, offset? }</c> answers <c>{ items[], total }</c>. A <c>directoryId</c> lists the Claude sessions of that
		/// directory's folder (the query filters them); <c>global: true</c> with a <c>query</c> lists the sessions of every project that match it.
		/// </summary>
		public const string ListMethod = "import.list";

		/// <summary>
		/// <c>import.titles { externalIds[] }</c> (at most 200) answers <c>{ titles[], pending }</c>: the titles known now. Titles that do not exist yet are generated in
		/// the background; ask again until <c>pending</c> is 0.
		/// </summary>
		public const string TitlesMethod = "import.titles";

		/// <summary>
		/// <c>import.run { targetId, items: [{ externalId, name? }] }</c> (at most 100 items) answers <c>{ imported[], failed[] }</c>. <c>targetId</c> is a directory or a
		/// group; every item is in exactly one of the two lists.
		/// </summary>
		public const string RunMethod = "import.run";
	}

	/// <summary>Schedules (FR.14). The older loopback methods <c>schedule.list/add/remove</c> keep their raw shapes; these are the DTO methods.</summary>
	public static class Schedules
	{
		/// <summary>The daemon serves <c>schedules.list</c>, <c>schedules.add</c>, <c>schedules.remove</c>, the <c>schedule.changed</c> notification and <see cref="SessionStateDto.HasSchedules"/>.</summary>
		public const string Capability = "schedules";

		/// <summary><c>schedules.list { nodeId? }</c> answers <c>{ schedules[] }</c>; with a <c>nodeId</c> only the schedules of that session.</summary>
		public const string ListMethod = "schedules.list";

		/// <summary>
		/// <c>schedules.add { nodeId, prompt, kind, fireAtUtc?, cron?, missedFire?, note?, maxFires? }</c> answers the new <c>ScheduleDto</c>. <c>delay</c> and <c>at</c>
		/// need <c>fireAtUtc</c>, <c>cron</c> needs <c>cron</c>. An invalid schedule is -32602.
		/// </summary>
		public const string AddMethod = "schedules.add";

		/// <summary><c>schedules.remove { scheduleId }</c> answers <c>{ removed }</c>.</summary>
		public const string RemoveMethod = "schedules.remove";

		/// <summary>Notification <c>{ nodeId? }</c>: a schedule was added, removed or fired. Local stream only, coalesced.</summary>
		public const string ChangedEvent = "schedule.changed";

		/// <summary>The values of <c>kind</c>.</summary>
		public static class Kinds
		{
			public const string Delay = "delay";
			public const string At = "at";
			public const string Cron = "cron";
		}

		/// <summary>The notifications that only the local stream carries (the relay filters them out).</summary>
		public static readonly IReadOnlyList<string> LocalOnlyEvents = [ChangedEvent];
	}

	/// <summary>The attention items of all sessions at once (FR.23).</summary>
	public static class AttentionAll
	{
		/// <summary>The daemon serves <c>attention.listAll</c>.</summary>
		public const string Capability = "attention.all";

		/// <summary>
		/// <c>attention.listAll { status?, kinds?, limit?, afterId? }</c> answers <c>{ items[], nextAfterId? }</c>. Without <c>status</c> only open items are listed;
		/// <c>kinds</c> narrows by kind; items are ordered by urgency, then newest. <c>afterId</c> is the <c>nextAfterId</c> of the previous page.
		/// </summary>
		public const string ListAllMethod = "attention.listAll";
	}

	/// <summary>The daemon's own health.</summary>
	public static class Health
	{
		/// <summary>The daemon serves <c>daemon.health</c>.</summary>
		public const string Capability = "daemon.health";

		/// <summary><c>daemon.health {}</c> answers the uptime, run id, event head, running sessions, index state, log size and memory.</summary>
		public const string Method = "daemon.health";

		/// <summary>The values of <see cref="DaemonIndexHealthDto.State"/>.</summary>
		public static class IndexStates
		{
			public const string Idle = "idle";
			public const string Scanning = "scanning";
		}
	}
}
