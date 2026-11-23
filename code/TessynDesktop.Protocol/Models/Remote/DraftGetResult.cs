using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>draft.get</c>: the unsent text of a session (empty when there is none) and its revision.</summary>
/// <param name="Text">The draft text; empty when the session has no draft.</param>
/// <param name="Revision">Identifies this text (and the questions); <see cref="RemoteProtocol.Composer.EmptyDraftRevision"/> when there is no draft. Pass it as <c>baseRevision</c> of <c>draft.set</c>.</param>
/// <param name="Questions">Only for the reply draft of a questionnaire item (FR.23.37): the saved question values; null when there are none.</param>
/// <remarks>Created by Claude</remarks>
public sealed record DraftGetResult(string Text, string Revision, IReadOnlyList<AttentionQuestionAnswerParams>? Questions = null);
