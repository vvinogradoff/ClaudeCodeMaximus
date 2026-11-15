using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// <c>import.list</c>, <c>import.titles</c> and <c>import.run</c> (package J4, FR.13): importing Claude Code sessions into the tree. Loopback only
/// (<c>remoteCallable: false</c>): the methods read the Claude project folders of this machine and add to the tree. The logic is
/// <see cref="IImportService"/>, the one implementation the Avalonia import picker uses too.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ImportRpcModule : IRemoteRpcModule
{
	private readonly IImportService _import;

	public ImportRpcModule(IImportService import)
	{
		_import = import;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.Add(RemoteProtocol.Import.ListMethod,      async (p, ct) => await ListAsync(p, ct),  remoteCallable: false);
		registry.AddSync(RemoteProtocol.Import.TitlesMethod, Titles,                                  remoteCallable: false);
		registry.Add(RemoteProtocol.Import.RunMethod,       async (p, ct) => await RunAsync(p, ct),   remoteCallable: false);
	}

	private async Task<object?> ListAsync(JsonNode? @params, System.Threading.CancellationToken cancellationToken) =>
		await _import.ListAsync(
			TdcpParams.OptionalString(@params, "directoryId"),
			TdcpParams.OptionalBool(@params, "global") ?? false,
			TdcpParams.OptionalString(@params, "query"),
			TdcpParams.OptionalInt(@params, "limit") ?? Constants.Import.ListDefaultLimit,
			TdcpParams.OptionalInt(@params, "offset") ?? 0,
			cancellationToken);

	private object? Titles(JsonNode? @params) => _import.GetTitles(TdcpListParams.RequireStrings(@params, "externalIds"));

	private async Task<object?> RunAsync(JsonNode? @params, System.Threading.CancellationToken cancellationToken) =>
		await _import.RunAsync(TdcpParams.RequireString(@params, "targetId"), ParseItems(TdcpParams.Find(@params, "items")), cancellationToken);

	private static List<ImportRequestDto> ParseItems(JsonNode? node)
	{
		if (node is not JsonArray array)
			throw new TdcpInvalidParamsException("'items' must be an array");

		var items = new List<ImportRequestDto>(array.Count);
		foreach (var element in array)
			items.Add(new ImportRequestDto(TdcpParams.RequireString(element, "externalId"), TdcpParams.OptionalString(element, "name")));

		return items;
	}
}
