using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>session.setOptions</c>.</summary>
/// <param name="Options">The options after the call.</param>
/// <param name="Corrections">The FR.11.12 notices of the mid-run corrections this call sent, e.g. <c>[AutoCommit was enabled for this run]</c>; omitted when none.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionSetOptionsResult(SessionOptionsDto Options, IReadOnlyList<string>? Corrections);
