namespace TessynDesktop.Models.Remote;

/// <summary>One answer of an attention.reply request.</summary>
/// <param name="ItemId">The Attention Item's id, e.g. "A12".</param>
/// <param name="Text">The user's answer.</param>
/// <remarks>Created by Claude</remarks>
public sealed record AttentionAnswerParams(string ItemId, string Text);
