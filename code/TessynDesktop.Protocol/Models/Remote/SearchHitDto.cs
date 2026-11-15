namespace TessynDesktop.Models.Remote;

/// <summary>One session found by <c>search.global</c> (package J4): the best-ranked message of the session.</summary>
/// <param name="ExternalId">The Claude session id (the JSONL file name).</param>
/// <param name="Title">The session's title when one is known.</param>
/// <param name="ProjectSlug">The Claude project the session lives under (a lossy form of the folder path, never the path).</param>
/// <param name="Snippet">The matching message, cut to a short excerpt.</param>
/// <param name="Score">Relevance, higher is better (the negated bm25 rank of the index).</param>
/// <param name="ImportedNodeId">The tree node that already imports the session, when there is one.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SearchHitDto(string ExternalId, string? Title, string ProjectSlug, string Snippet, double Score, string? ImportedNodeId);
