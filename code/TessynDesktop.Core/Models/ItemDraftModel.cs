using System.Collections.Generic;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Models;

/// <summary>The reply draft of one Focus item (FR.23.19, FR.23.37): the note text and, for a questionnaire item, the question values.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ItemDraftModel(string Text, IReadOnlyList<AttentionQuestionAnswerParams>? Questions);
