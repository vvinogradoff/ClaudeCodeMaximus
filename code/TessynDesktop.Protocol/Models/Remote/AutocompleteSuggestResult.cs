using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>autocomplete.suggest</c>.</summary>
/// <param name="Mode">One of <see cref="RemoteProtocol.Composer.AutocompleteModes"/>; <c>none</c> when the caret is not in a trigger.</param>
/// <param name="TriggerStart">Index in the text where the trigger (<c>#</c>, <c>##</c> or the path) starts.</param>
/// <param name="TriggerLength">Length of the trigger text, to be replaced by the accepted suggestion's <c>insertText</c>.</param>
/// <param name="IndexReady">False while the code index of the session's directory is still being built (symbol and file modes): the list is empty then, ask again shortly.</param>
/// <param name="Items">The suggestions, capped.</param>
/// <remarks>Created by Claude</remarks>
public sealed record AutocompleteSuggestResult(string Mode, int TriggerStart, int TriggerLength, bool IndexReady, IReadOnlyList<AutocompleteItemDto> Items);
