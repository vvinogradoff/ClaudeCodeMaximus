using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>tree.search</c> (contract 3.4).</summary>
/// <param name="Query">The trimmed query.</param>
/// <param name="EffectiveExact">Whether only strict matches were kept.</param>
/// <param name="AutoExact">Whether the auto-exact rule (FR.1.14) applies to this query.</param>
/// <param name="UsedFallback">The search index was unavailable and the file scan answered instead.</param>
/// <param name="Matches">Matching top-level sessions, most relevant first.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TreeSearchResult(string Query, bool EffectiveExact, bool AutoExact, bool UsedFallback, IReadOnlyList<TreeSearchMatchDto> Matches);
