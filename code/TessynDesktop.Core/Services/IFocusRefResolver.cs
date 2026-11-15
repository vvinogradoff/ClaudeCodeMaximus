using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>What a session gave for one reference of a <c>post_attention</c> item (already shape-checked by the tool layer).</summary>
/// <param name="Kind">"entry", "file" or "url".</param>
/// <param name="Session">entry: short id or node id of the session; null = the caller.</param>
/// <param name="Quote">entry: text of the entry, at most 300 characters.</param>
/// <param name="Occurrence">entry: "last" (default) or "first".</param>
/// <param name="Path">file: absolute, or relative to the caller's working directory.</param>
/// <param name="Url">url: http or https.</param>
/// <param name="Label">Optional short text.</param>
/// <remarks>Created by Claude</remarks>
public sealed record FocusRefRequest(
	string Kind,
	string? Session = null,
	string? Quote = null,
	string? Occurrence = null,
	string? Path = null,
	string? Url = null,
	string? Label = null);

/// <summary>A reference that cannot be resolved; the message names the cause so the posting model can fix it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusRefException(string message) : System.Exception(message);

/// <summary>Resolves the references of a Focus item when it is posted (FR.23 amendment).</summary>
/// <remarks>Created by Claude</remarks>
public interface IFocusRefResolver
{
	/// <summary>
	/// The stored form of <paramref name="request"/>: an entry reference becomes node id, file id, entry id and the offset and length of
	/// the quote in the entry; a file reference an opaque id plus name, size, type and time; a URL stays as given.
	/// Throws <see cref="FocusRefException"/> when it cannot be resolved.
	/// </summary>
	AttentionRefModel Resolve(SessionNodeModel caller, FocusRefRequest request);
}
