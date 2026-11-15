namespace TessynDesktop.Models.Remote;

/// <summary>One suggestion of <c>autocomplete.suggest</c>.</summary>
/// <param name="Label">What the list shows.</param>
/// <param name="InsertText">What replaces the trigger text when the suggestion is accepted.</param>
/// <param name="Kind">Symbol kind (<c>class</c>, <c>method</c>, ...) for a symbol; <c>file</c> or <c>directory</c> otherwise.</param>
/// <param name="Detail">Secondary text: the relative path of a file, the namespace of a symbol, the extension of a path entry; null when there is none.</param>
/// <param name="MatchStart">Offset of the typed query inside <paramref name="Label"/>, for highlighting.</param>
/// <param name="MatchLength">Length of the typed query.</param>
/// <remarks>Created by Claude</remarks>
public sealed record AutocompleteItemDto(string Label, string InsertText, string Kind, string? Detail, int MatchStart, int MatchLength);
