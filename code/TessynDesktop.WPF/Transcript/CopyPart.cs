namespace TessynDesktop.WPF.Transcript;

/// <summary>One piece of a Copy as Markdown request (FR.2.13): the part of the selection that lies in one entry.</summary>
/// <param name="Entry">The entry's index, or null for text outside the entries (the live row).</param>
/// <param name="Whole">The selection covers the entry's whole text: its original Markdown replaces <paramref name="Markdown"/>.</param>
/// <param name="Markdown">The Markdown the page converted from the selected fragment.</param>
/// <remarks>Created by Claude</remarks>
public sealed record CopyPart(int? Entry, bool Whole, string Markdown);
