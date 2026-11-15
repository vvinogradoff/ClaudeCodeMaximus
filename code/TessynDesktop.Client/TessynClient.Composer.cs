using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

public sealed partial class TessynClient
{
	/// <inheritdoc />
	public Task<DraftGetResult> DraftGetAsync(string nodeId, CancellationToken cancellationToken = default) =>
		CallAsync<DraftGetResult>(RemoteProtocol.Composer.DraftGetMethod, Params(("nodeId", Node(nodeId))), cancellationToken);

	/// <inheritdoc />
	public Task<DraftSetResult> DraftSetAsync(string nodeId, string text, string? baseRevision = null, CancellationToken cancellationToken = default) =>
		CallAsync<DraftSetResult>(
			RemoteProtocol.Composer.DraftSetMethod,
			Params(("nodeId", Node(nodeId)), ("text", Node(text)), ("baseRevision", Node(baseRevision))),
			cancellationToken);

	/// <inheritdoc />
	public Task<DraftGetResult> ReplyDraftGetAsync(string nodeId, string itemId, CancellationToken cancellationToken = default) =>
		CallAsync<DraftGetResult>(RemoteProtocol.Composer.DraftGetMethod, Params(("nodeId", Node(nodeId)), ("itemId", Node(itemId))), cancellationToken);

	/// <inheritdoc />
	public Task<DraftSetResult> ReplyDraftSetAsync(string nodeId, string itemId, string text, string? baseRevision = null, CancellationToken cancellationToken = default) =>
		CallAsync<DraftSetResult>(
			RemoteProtocol.Composer.DraftSetMethod,
			Params(("nodeId", Node(nodeId)), ("itemId", Node(itemId)), ("text", Node(text)), ("baseRevision", Node(baseRevision))),
			cancellationToken);

	/// <inheritdoc />
	public Task<DraftDeleteResult> DraftDeleteAsync(string nodeId, CancellationToken cancellationToken = default) =>
		CallAsync<DraftDeleteResult>(RemoteProtocol.Composer.DraftDeleteMethod, Params(("nodeId", Node(nodeId))), cancellationToken);

	/// <inheritdoc />
	public Task<AutocompleteSuggestResult> AutocompleteSuggestAsync(string nodeId, string text, int caret, CancellationToken cancellationToken = default) =>
		CallAsync<AutocompleteSuggestResult>(
			RemoteProtocol.Composer.AutocompleteSuggestMethod,
			Params(("nodeId", Node(nodeId)), ("text", Node(text)), ("caret", Node(caret))),
			cancellationToken);
}
