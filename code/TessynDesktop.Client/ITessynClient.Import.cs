using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

/// <summary>
/// Package J4: global search, session import, schedules, the attention list of all sessions and the daemon health. All are loopback only; the daemon
/// announces them with the capabilities <see cref="RemoteProtocol.Search.Capability"/>, <see cref="RemoteProtocol.Import.Capability"/>,
/// <see cref="RemoteProtocol.Schedules.Capability"/>, <see cref="RemoteProtocol.AttentionAll.Capability"/> and <see cref="RemoteProtocol.Health.Capability"/>.
/// <c>schedule.changed</c> arrives as a notification (<see cref="ScheduleNotificationExtensions"/>). A refusal is a <see cref="TessynRpcException"/> whose message is the reason.
/// </summary>
public partial interface ITessynClient
{
	/// <summary><c>search.global</c>: the sessions of every project whose messages match, best first, one hit per session.</summary>
	Task<SearchGlobalResult> SearchGlobalAsync(string query, string? projectSlug = null, int? limit = null, int? offset = null, CancellationToken cancellationToken = default);

	/// <summary><c>search.reindex</c>: starts a rescan of the session index in the background; <c>index.changed</c> follows.</summary>
	Task<SearchReindexResult> SearchReindexAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// <c>import.list</c>: the Claude sessions that can be imported. With a <paramref name="directoryId"/> those of that directory's folder (the query filters them);
	/// with <paramref name="global"/> and a query, the sessions of every project that match it.
	/// </summary>
	Task<ImportListResult> ImportListAsync(string? directoryId = null, bool global = false, string? query = null, int? limit = null, int? offset = null, CancellationToken cancellationToken = default);

	/// <summary><c>import.titles</c>: the titles known now; missing ones are generated in the background, so ask again while <see cref="ImportTitlesResult.Pending"/> is above 0.</summary>
	Task<ImportTitlesResult> ImportTitlesAsync(IReadOnlyList<string> externalIds, CancellationToken cancellationToken = default);

	/// <summary><c>import.run</c>: imports the sessions under a directory or a group; each is answered as imported or failed.</summary>
	Task<ImportRunResult> ImportRunAsync(string targetId, IReadOnlyList<ImportRequestDto> items, CancellationToken cancellationToken = default);

	/// <summary><c>schedules.list</c>: the schedules of a session, or of all sessions.</summary>
	Task<SchedulesListResult> SchedulesListAsync(string? nodeId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// <c>schedules.add</c>: <see cref="ScheduleKindDto.Delay"/> and <see cref="ScheduleKindDto.At"/> need <paramref name="fireAtUtc"/>, <see cref="ScheduleKindDto.Cron"/> needs
	/// <paramref name="cron"/>. An invalid schedule is a <see cref="TessynRpcException"/> naming the field. <paramref name="profileId"/>, <paramref name="modelId"/> and
	/// <paramref name="effort"/> are what the user picked when scheduling (A14): the turn runs on them when it fires; null = not picked, not sent.
	/// </summary>
	Task<ScheduleDto> SchedulesAddAsync(string nodeId, string prompt, ScheduleKindDto kind, DateTimeOffset? fireAtUtc = null, string? cron = null, MissedFireDto? missedFire = null, string? note = null, int? maxFires = null, string? profileId = null, string? modelId = null, string? effort = null, CancellationToken cancellationToken = default);

	/// <summary><c>schedules.remove</c>.</summary>
	Task<SchedulesRemoveResult> SchedulesRemoveAsync(string scheduleId, CancellationToken cancellationToken = default);

	/// <summary>
	/// <c>attention.listAll</c>: the attention items of every session, most urgent first. <paramref name="status"/> defaults to open; <paramref name="afterId"/> is the
	/// <see cref="AttentionListAllResult.NextAfterId"/> of the previous page.
	/// </summary>
	Task<AttentionListAllResult> AttentionListAllAsync(AttentionStatus? status = null, IReadOnlyList<AttentionKind>? kinds = null, int? limit = null, string? afterId = null, CancellationToken cancellationToken = default);

	/// <summary><c>daemon.health</c>: uptime, run id, event head, running sessions, index state, log size and memory.</summary>
	Task<DaemonHealthResult> DaemonHealthAsync(CancellationToken cancellationToken = default);
}
