namespace TessynDesktop.Models.Remote;

/// <summary>The optional <c>range</c> of <c>session.entry</c> (P1.F): a slice of the entry's content, in characters.</summary>
/// <param name="Offset">First character of the slice (default 0 on the wire).</param>
/// <param name="Length">Most characters to return (default and cap: <c>Constants.Remote.Limits.EntryContentMaxChars</c>).</param>
/// <remarks>Created by Claude</remarks>
public sealed record EntryRangeParams(int Offset = 0, int? Length = null);
