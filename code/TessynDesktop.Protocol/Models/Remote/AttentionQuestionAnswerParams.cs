using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>The current answer to one question of a questionnaire item (FR.23.36, FR.23.37): in an attention.reply answer and in a reply draft.</summary>
/// <param name="Number">The question's number, 1-based.</param>
/// <param name="Value">"yes" or "no" for YesNo, a choice label for Single, the text for Text. Null means no answer.</param>
/// <param name="Choices">Multi only: the selected choice labels. Null means no answer; an empty list means none selected.</param>
/// <remarks>Created by Claude</remarks>
public sealed record AttentionQuestionAnswerParams(int Number, string? Value = null, IReadOnlyList<string>? Choices = null);
