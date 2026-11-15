namespace TessynDesktop.WPF.Services;

/// <summary>The outcome of reading a file for the preview pane.</summary>
/// <param name="Name">File name (no directory).</param>
/// <param name="Mime">MIME type from the daemon.</param>
/// <param name="Kind">How the file is shown.</param>
/// <param name="Size">File size in bytes.</param>
/// <param name="Text">The content of a text or markdown file (possibly cut, see <paramref name="Truncated"/>); null for other kinds.</param>
/// <param name="FilePath">The cached copy of an image, video or other binary file; null for text.</param>
/// <param name="Truncated">A text file was longer than the limit; only its start is in <paramref name="Text"/>.</param>
/// <param name="Refusal">Why nothing was read (too large, no preview for this type); null when the file was read.</param>
/// <remarks>Created by Claude</remarks>
public sealed record LoadedPreview(string Name, string Mime, PreviewKind Kind, long Size, string? Text, string? FilePath, bool Truncated, string? Refusal);
