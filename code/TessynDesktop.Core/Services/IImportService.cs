using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Imports Claude Code sessions into the tree (FR.13): the one implementation behind the Avalonia import picker and the remote
/// <c>import.*</c> methods. It lists what can be imported, supplies titles, and turns a Claude JSONL session into a session file and a tree node.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IImportService
{
	/// <summary>
	/// Imports one session under a directory or a group: parses its JSONL file into a new session file, adds the node (named
	/// <paramref name="name"/>, else the generated title, else the first prompt cut to 60 characters, else "Imported Session"), and records the folder
	/// a cross-project session came from. Never throws: a failure is logged and reported in the outcome.
	/// </summary>
	ImportOutcomeModel ImportSession(DirectoryNodeModel? directory, GroupNodeModel? group, ClaudeSessionSummaryModel summary, string? name = null);

	/// <summary>
	/// The folder a Claude project slug stands for. A slug is lossy (every character that is not a letter, digit or '-' became '-'), so the answer comes
	/// from, in order: the <c>cwd</c> recorded in the JSONL file, a directory of the tree that has this slug, and last a reconstruction that is
	/// readable and maps back to the same slug.
	/// </summary>
	string ResolveOriginalProjectPath(string projectSlug, string? jsonlPath);

	/// <summary>Lists the sessions that can be imported: those of a directory's folder, or, with <paramref name="global"/>, the sessions of every project that match <paramref name="query"/>.</summary>
	Task<ImportListResult> ListAsync(string? directoryId, bool global, string? query, int limit, int offset, CancellationToken cancellationToken = default);

	/// <summary>The titles known now for the given sessions; titles that do not exist yet are generated in the background.</summary>
	ImportTitlesResult GetTitles(IReadOnlyList<string> externalIds);

	/// <summary>Imports the requested sessions under the directory or group <paramref name="targetId"/>; every request is answered as imported or failed.</summary>
	Task<ImportRunResult> RunAsync(string targetId, IReadOnlyList<ImportRequestDto> items, CancellationToken cancellationToken = default);
}
