using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Answer of attention.reply.</summary>
/// <param name="AnsweredIds">Items that were open and are now answered.</param>
/// <param name="SkippedIds">Requested items that were unknown, already closed, repeated or had a blank answer.</param>
/// <param name="TurnStarted">A turn carrying the answers was started.</param>
/// <remarks>Created by Claude</remarks>
public sealed record AttentionReplyResult(IReadOnlyList<string> AnsweredIds, IReadOnlyList<string> SkippedIds, bool TurnStarted);
