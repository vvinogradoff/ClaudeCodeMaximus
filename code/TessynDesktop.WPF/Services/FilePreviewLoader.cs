using System.Text;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// <see cref="IFilePreviewLoader"/> over <c>file.read</c>. The first chunk tells the name, type and size, which decide the kind and
/// the limit before more is read. Text is capped at <see cref="Constants.Limits.MaxPreviewTextBytes"/> (the rest is dropped, the
/// result says so); images and video over their limit are refused; unknown types are not read to the end. Binary content is written
/// to disk only for image and video previews and, on request, for files <see cref="ExternalOpenPolicy"/> allows, so nothing else
/// ever reaches the disk.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FilePreviewLoader : IFilePreviewLoader
{
	private readonly ITessynClient _client;
	private readonly FileKindClassifier _classifier;
	private readonly PreviewFileStore _store;
	private readonly ExternalOpenPolicy _openPolicy;

	public FilePreviewLoader(ITessynClient client, FileKindClassifier classifier, PreviewFileStore store, ExternalOpenPolicy openPolicy)
	{
		_client = client;
		_classifier = classifier;
		_store = store;
		_openPolicy = openPolicy;
	}

	public async Task<LoadedPreview> LoadAsync(string handle, bool saveToFile, IProgress<PreviewProgress>? progress, CancellationToken cancellationToken)
	{
		var first = await _client.FileReadAsync(handle, 0, Constants.Limits.PreviewChunkBytes, cancellationToken);
		var kind = _classifier.Classify(first.Name, first.Mime);
		progress?.Report(new PreviewProgress(0, first.Size));

		if (kind == PreviewKind.Unsupported)
			return Refused(first, kind, Constants.Texts.PreviewUnsupported);
		if (kind == PreviewKind.Pdf && !saveToFile)
			return new LoadedPreview(first.Name, first.Mime, kind, first.Size, null, null, false, null);
		if (saveToFile && !_openPolicy.IsAllowed(first.Name))
			return Refused(first, kind, Constants.Texts.PreviewUnsupported);

		var textKind = kind is PreviewKind.Text or PreviewKind.Markdown;
		var isText = !saveToFile && textKind;
		var limit = saveToFile && textKind ? Constants.Limits.MaxPreviewPdfBytes : _classifier.LimitOf(kind);
		if (!isText && first.Size > limit)
			return Refused(first, kind, $"{Constants.Texts.PreviewTooLarge} ({FormatSize(first.Size)}, limit {FormatSize(limit)}).");

		// The cache file is always "content" + the checked extension; the display name never reaches the disk (V9). The type was checked above.
		var extension = isText ? null : _openPolicy.ExtensionOf(first.Name);
		if (!isText && (extension == null || !Constants.FileTypes.ExternallyOpenable.Contains(extension)))
			return Refused(first, kind, Constants.Texts.PreviewUnsupported);

		var path = isText ? null : _store.NewFilePath(extension!);
		using var memory = isText ? new MemoryStream() : null;
		FileStream? file = null;
		var truncated = false;
		try
		{
			file = path == null ? null : new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
			Stream sink = (Stream?)memory ?? file!;
			var chunk = first;
			var read = 0L;
			var cap = Math.Max(limit, first.Size);   // real bytes are counted below; a daemon cannot make the cache grow past what it declared
			for (var chunks = 1; ; chunks++)
			{
				var bytes = chunk.Encoding == "base64" ? Convert.FromBase64String(chunk.Data) : Encoding.UTF8.GetBytes(chunk.Data);
				if (bytes.Length != chunk.Length)
					throw new InvalidOperationException("The daemon's chunk length does not match its data.");
				if (chunks > Constants.Limits.MaxPreviewChunks)
					throw new InvalidOperationException("The daemon sent too many chunks.");

				read += bytes.Length;
				if (!isText && read > cap)
					throw new InvalidOperationException("The daemon sent more data than the file's declared size.");

				await sink.WriteAsync(bytes, cancellationToken);
				progress?.Report(new PreviewProgress(read, first.Size));

				if (chunk.Eof)
					break;
				if (isText && read >= Constants.Limits.MaxPreviewTextBytes)
				{
					truncated = true;
					break;
				}

				if (bytes.Length == 0)
					throw new InvalidOperationException("The daemon returned an empty chunk before the end of the file.");

				cancellationToken.ThrowIfCancellationRequested();
				chunk = await _client.FileReadAsync(handle, chunk.Offset + chunk.Length, Constants.Limits.PreviewChunkBytes, cancellationToken);
			}

			if (isText)
				return new LoadedPreview(first.Name, first.Mime, kind, first.Size, new UTF8Encoding(false).GetString(memory!.GetBuffer(), 0, (int)memory.Length), null, truncated, null);

			await file!.DisposeAsync();
			file = null;
			return new LoadedPreview(first.Name, first.Mime, kind, first.Size, null, path, false, null);
		}
		catch
		{
			if (file != null)
				await file.DisposeAsync();
			_store.Delete(path);
			throw;
		}
	}

	private static LoadedPreview Refused(FileReadResult first, PreviewKind kind, string reason) =>
		new(first.Name, first.Mime, kind, first.Size, null, null, false, reason);

	private static string FormatSize(long bytes) => bytes switch
	{
		>= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB",
		>= 1024L * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
		>= 1024 => $"{bytes / 1024.0:0.#} KB",
		_ => $"{bytes} B",
	};
}
