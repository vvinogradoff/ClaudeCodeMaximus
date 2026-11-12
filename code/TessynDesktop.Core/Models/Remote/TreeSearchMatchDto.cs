using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>One matching top-level session of <c>tree.search</c> (contract 3.4).</summary>
/// <param name="Id">The TOP-LEVEL session's NodeId (workers roll up onto it).</param>
/// <param name="Strict">Messages containing the query as an exact phrase, from the best key among the session and its workers.</param>
/// <param name="Loose">Messages containing all query words, from the same best key.</param>
/// <param name="MatchedSelf">The session itself matched, not only one of its workers.</param>
/// <param name="WorkerIds">NodeIds of the matching sessions below it, in tree order.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TreeSearchMatchDto(string Id, int Strict, int Loose, bool MatchedSelf, IReadOnlyList<string> WorkerIds);
