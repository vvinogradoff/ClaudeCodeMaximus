using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>One answer of an attention.reply request.</summary>
/// <param name="ItemId">The Attention Item's id, e.g. "A12".</param>
/// <param name="Text">The user's answer; for a questionnaire item the optional note (FR.23.35).</param>
/// <param name="Questions">The current value of every question of a questionnaire item (FR.23.36); null for other items.</param>
/// <remarks>Created by Claude</remarks>
public sealed record AttentionAnswerParams(string ItemId, string Text, IReadOnlyList<AttentionQuestionAnswerParams>? Questions = null);
