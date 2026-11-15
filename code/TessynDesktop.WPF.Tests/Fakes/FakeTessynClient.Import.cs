using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>
/// The search, import, schedule, attention-list and health methods of the fake (P1.J4). Every call is recorded in <see cref="Calls"/> as "method:argument";
/// each method answers from its handler when one is set, otherwise with an empty success. A handler may throw to script a refusal.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FakeTessynClient
{
	public Func<string, string?, int?, int?, SearchGlobalResult>? SearchGlobalHandler { get; set; }

	public Func<SearchReindexResult>? SearchReindexHandler { get; set; }

	public Func<string?, bool, string?, int?, int?, ImportListResult>? ImportListHandler { get; set; }

	public Func<IReadOnlyList<string>, ImportTitlesResult>? ImportTitlesHandler { get; set; }

	public Func<string, IReadOnlyList<ImportRequestDto>, ImportRunResult>? ImportRunHandler { get; set; }

	public Func<string?, SchedulesListResult>? SchedulesListHandler { get; set; }

	public Func<string, string, ScheduleKindDto, DateTimeOffset?, string?, MissedFireDto?, string?, int?, ScheduleDto>? SchedulesAddHandler { get; set; }

	public Func<string, SchedulesRemoveResult>? SchedulesRemoveHandler { get; set; }

	public Func<AttentionStatus?, IReadOnlyList<AttentionKind>?, int?, string?, AttentionListAllResult>? AttentionListAllHandler { get; set; }

	public Func<DaemonHealthResult>? DaemonHealthHandler { get; set; }

	public Task<SearchGlobalResult> SearchGlobalAsync(string query, string? projectSlug = null, int? limit = null, int? offset = null, CancellationToken cancellationToken = default) =>
		Record($"search.global:{query}", () => SearchGlobalHandler?.Invoke(query, projectSlug, limit, offset) ?? new SearchGlobalResult([], 0));

	public Task<SearchReindexResult> SearchReindexAsync(CancellationToken cancellationToken = default) =>
		Record("search.reindex:", () => SearchReindexHandler?.Invoke() ?? new SearchReindexResult(true));

	public Task<ImportListResult> ImportListAsync(string? directoryId = null, bool global = false, string? query = null, int? limit = null, int? offset = null, CancellationToken cancellationToken = default) =>
		Record($"import.list:{directoryId}:{global}:{query}", () => ImportListHandler?.Invoke(directoryId, global, query, limit, offset) ?? new ImportListResult([], 0));

	public Task<ImportTitlesResult> ImportTitlesAsync(IReadOnlyList<string> externalIds, CancellationToken cancellationToken = default) =>
		Record($"import.titles:{string.Join(",", externalIds)}", () => ImportTitlesHandler?.Invoke(externalIds) ?? new ImportTitlesResult([], 0));

	public Task<ImportRunResult> ImportRunAsync(string targetId, IReadOnlyList<ImportRequestDto> items, CancellationToken cancellationToken = default) =>
		Record($"import.run:{targetId}:{string.Join(",", items.Select(i => i.ExternalId))}", () =>
			ImportRunHandler?.Invoke(targetId, items) ?? new ImportRunResult([.. items.Select(i => new ImportedSessionDto("node-" + i.ExternalId, i.ExternalId))], []));

	public Task<SchedulesListResult> SchedulesListAsync(string? nodeId = null, CancellationToken cancellationToken = default) =>
		Record($"schedules.list:{nodeId}", () => SchedulesListHandler?.Invoke(nodeId) ?? new SchedulesListResult([]));

	public Task<ScheduleDto> SchedulesAddAsync(string nodeId, string prompt, ScheduleKindDto kind, DateTimeOffset? fireAtUtc = null, string? cron = null, MissedFireDto? missedFire = null, string? note = null, int? maxFires = null, CancellationToken cancellationToken = default) =>
		Record($"schedules.add:{nodeId}:{kind}", () =>
			SchedulesAddHandler?.Invoke(nodeId, prompt, kind, fireAtUtc, cron, missedFire, note, maxFires)
			?? new ScheduleDto("sched-1", nodeId, kind, prompt, note ?? string.Empty, fireAtUtc, cron, missedFire ?? MissedFireDto.FireOnce, 0, maxFires ?? 0, false));

	public Task<SchedulesRemoveResult> SchedulesRemoveAsync(string scheduleId, CancellationToken cancellationToken = default) =>
		Record($"schedules.remove:{scheduleId}", () => SchedulesRemoveHandler?.Invoke(scheduleId) ?? new SchedulesRemoveResult(true));

	public Task<AttentionListAllResult> AttentionListAllAsync(AttentionStatus? status = null, IReadOnlyList<AttentionKind>? kinds = null, int? limit = null, string? afterId = null, CancellationToken cancellationToken = default) =>
		Record($"attention.listAll:{status}:{afterId}", () => AttentionListAllHandler?.Invoke(status, kinds, limit, afterId) ?? new AttentionListAllResult([], null));

	public Task<DaemonHealthResult> DaemonHealthAsync(CancellationToken cancellationToken = default) =>
		Record("daemon.health:", () => DaemonHealthHandler?.Invoke()
			?? new DaemonHealthResult(0, "run-fake", 0, 0, new DaemonIndexHealthDto("idle", null, 0), null, 0, 0));
}
