using System.Text.Json;
using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.Client;

public sealed partial class TessynClient
{
	/// <inheritdoc />
	public Task<SearchGlobalResult> SearchGlobalAsync(string query, string? projectSlug = null, int? limit = null, int? offset = null, CancellationToken cancellationToken = default) =>
		CallAsync<SearchGlobalResult>(RemoteProtocol.Search.GlobalMethod, Params(("query", Node(query)), ("projectSlug", Node(projectSlug)), ("limit", Node(limit)), ("offset", Node(offset))), cancellationToken);

	/// <inheritdoc />
	public Task<SearchReindexResult> SearchReindexAsync(CancellationToken cancellationToken = default) =>
		CallAsync<SearchReindexResult>(RemoteProtocol.Search.ReindexMethod, null, cancellationToken);

	/// <inheritdoc />
	public Task<ImportListResult> ImportListAsync(string? directoryId = null, bool global = false, string? query = null, int? limit = null, int? offset = null, CancellationToken cancellationToken = default) =>
		CallAsync<ImportListResult>(
			RemoteProtocol.Import.ListMethod,
			Params(("directoryId", Node(directoryId)), ("global", global ? Node(true) : null), ("query", Node(query)), ("limit", Node(limit)), ("offset", Node(offset))),
			cancellationToken);

	/// <inheritdoc />
	public Task<ImportTitlesResult> ImportTitlesAsync(IReadOnlyList<string> externalIds, CancellationToken cancellationToken = default) =>
		CallAsync<ImportTitlesResult>(RemoteProtocol.Import.TitlesMethod, Params(("externalIds", ToNode(externalIds))), cancellationToken);

	/// <inheritdoc />
	public Task<ImportRunResult> ImportRunAsync(string targetId, IReadOnlyList<ImportRequestDto> items, CancellationToken cancellationToken = default) =>
		CallAsync<ImportRunResult>(RemoteProtocol.Import.RunMethod, Params(("targetId", Node(targetId)), ("items", ToNode(items))), cancellationToken);

	/// <inheritdoc />
	public Task<SchedulesListResult> SchedulesListAsync(string? nodeId = null, CancellationToken cancellationToken = default) =>
		CallAsync<SchedulesListResult>(RemoteProtocol.Schedules.ListMethod, Params(("nodeId", Node(nodeId))), cancellationToken);

	/// <inheritdoc />
	public Task<ScheduleDto> SchedulesAddAsync(string nodeId, string prompt, ScheduleKindDto kind, DateTimeOffset? fireAtUtc = null, string? cron = null, MissedFireDto? missedFire = null, string? note = null, int? maxFires = null, string? profileId = null, string? modelId = null, string? effort = null, CancellationToken cancellationToken = default) =>
		CallAsync<ScheduleDto>(
			RemoteProtocol.Schedules.AddMethod,
			Params(
				("nodeId", Node(nodeId)),
				("prompt", Node(prompt)),
				("kind", Node(WireName(kind))),
				("fireAtUtc", Node(fireAtUtc?.ToUniversalTime().ToString("O"))),
				("cron", Node(cron)),
				("missedFire", Node(missedFire is { } policy ? WireName(policy) : null)),
				("note", Node(note)),
				("maxFires", Node(maxFires)),
				("profileId", Node(profileId)),
				("modelId", Node(modelId)),
				("effort", Node(effort))),
			cancellationToken);

	/// <inheritdoc />
	public Task<SchedulesRemoveResult> SchedulesRemoveAsync(string scheduleId, CancellationToken cancellationToken = default) =>
		CallAsync<SchedulesRemoveResult>(RemoteProtocol.Schedules.RemoveMethod, Params(("scheduleId", Node(scheduleId))), cancellationToken);

	/// <inheritdoc />
	public Task<AttentionListAllResult> AttentionListAllAsync(AttentionStatus? status = null, IReadOnlyList<AttentionKind>? kinds = null, int? limit = null, string? afterId = null, CancellationToken cancellationToken = default) =>
		CallAsync<AttentionListAllResult>(
			RemoteProtocol.AttentionAll.ListAllMethod,
			Params(
				("status", Node(status is { } s ? WireName(s) : null)),
				("kinds", kinds == null ? null : ToNode<string>([.. kinds.Select(WireName)])),
				("limit", Node(limit)),
				("afterId", Node(afterId))),
			cancellationToken);

	/// <inheritdoc />
	public Task<DaemonHealthResult> DaemonHealthAsync(CancellationToken cancellationToken = default) =>
		CallAsync<DaemonHealthResult>(RemoteProtocol.Health.Method, null, cancellationToken);

	private static JsonNode ToNode<T>(IReadOnlyList<T> values) => JsonSerializer.SerializeToNode(values, TdcpJsonOptions.Default)!;

	/// <summary>An enum value as it goes on the wire (camelCase, as the daemon JSON options write it).</summary>
	private static string WireName<T>(T value) where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString()!);
}
