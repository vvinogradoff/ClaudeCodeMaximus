namespace TessynDesktop.WPF.Services;

/// <summary>What the preview pane is asked to open: a <c>file.read</c> handle and what is known about the file before it is read.</summary>
/// <param name="Handle">A file reference's <c>refId</c> or the handle of <c>file.preview</c>.</param>
/// <param name="Name">File name for the title.</param>
/// <remarks>Created by Claude</remarks>
public sealed record PreviewTarget(string Handle, string Name);
