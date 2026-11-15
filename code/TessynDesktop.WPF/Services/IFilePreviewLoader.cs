namespace TessynDesktop.WPF.Services;

/// <summary>Reads the file behind a <c>file.read</c> handle for the preview pane, in chunks, with progress and cancellation.</summary>
/// <remarks>Created by Claude</remarks>
public interface IFilePreviewLoader
{
	/// <summary>
	/// Reads a file. Without <paramref name="saveToFile"/>: text and markdown come back as text (cut at the text limit), images and
	/// video are written to the preview cache, a PDF is not read at all. With it (to open the file in the default app): the whole file
	/// is written to the cache, but only when its type is on the <see cref="ExternalOpenPolicy"/> allowlist. A file over its limit or
	/// of no known type is not read to the end: the result carries a <see cref="LoadedPreview.Refusal"/>.
	/// </summary>
	Task<LoadedPreview> LoadAsync(string handle, bool saveToFile, IProgress<PreviewProgress>? progress, CancellationToken cancellationToken);
}
