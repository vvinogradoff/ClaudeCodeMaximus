using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>What the Focus view keeps locally of a card that left the list for a moment: its note text and, on a questionnaire card, its changed question values (FR.23.37).</summary>
/// <param name="Text">The reply box text.</param>
/// <param name="Questions">The question values; null when every question was still at its start.</param>
/// <remarks>Created by Claude</remarks>
public sealed record FocusReplyDraft(string Text, IReadOnlyList<AttentionQuestionAnswerParams>? Questions);
