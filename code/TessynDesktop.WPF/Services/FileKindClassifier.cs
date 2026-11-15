namespace TessynDesktop.WPF.Services;

/// <summary>Decides how a file is previewed, from its name first and its MIME type second; also the size limit per kind.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FileKindClassifier
{
	/// <summary>The preview kind of a file.</summary>
	public PreviewKind Classify(string? name, string? mime)
	{
		var extension = ExtensionOf(name);
		if (extension != null)
		{
			if (Constants.FileTypes.Markdown.Contains(extension))
				return PreviewKind.Markdown;
			if (Constants.FileTypes.Image.Contains(extension))
				return PreviewKind.Image;
			if (Constants.FileTypes.Video.Contains(extension))
				return PreviewKind.Video;
			if (Constants.FileTypes.Pdf.Contains(extension))
				return PreviewKind.Pdf;
			if (Constants.FileTypes.Text.Contains(extension))
				return PreviewKind.Text;
		}

		// svg is an image type by MIME but must never be rendered: it is text here (and was caught above by extension when named).
		var type = (mime ?? string.Empty).Trim().ToLowerInvariant();
		if (type.StartsWith("image/svg", StringComparison.Ordinal))
			return PreviewKind.Text;
		if (type == "text/markdown")
			return PreviewKind.Markdown;
		if (type.StartsWith("text/", StringComparison.Ordinal) || type == "application/json" || type == "application/xml")
			return PreviewKind.Text;
		if (type is "image/png" or "image/jpeg" or "image/gif" or "image/webp" or "image/bmp")
			return PreviewKind.Image;
		if (type is "video/mp4" or "video/webm" or "video/quicktime")
			return PreviewKind.Video;
		if (type == "application/pdf")
			return PreviewKind.Pdf;

		return PreviewKind.Unsupported;
	}

	/// <summary>The most bytes the client reads for a preview of this kind.</summary>
	public long LimitOf(PreviewKind kind) => kind switch
	{
		PreviewKind.Markdown or PreviewKind.Text => Constants.Limits.MaxPreviewTextBytes,
		PreviewKind.Image => Constants.Limits.MaxPreviewImageBytes,
		PreviewKind.Video => Constants.Limits.MaxPreviewVideoBytes,
		PreviewKind.Pdf => Constants.Limits.MaxPreviewPdfBytes,
		_ => 0,
	};

	private static string? ExtensionOf(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return null;

		var file = name[(name.LastIndexOfAny(['\\', '/']) + 1)..].TrimEnd('.', ' ');
		var dot = file.LastIndexOf('.');
		if (dot < 0)
			return null;

		var extension = file[dot..];
		return extension.Any(c => c > 127) ? null : extension;   // the sets compare ordinal-ignore-case; no culture mapping can turn a look-alike into an allowed type
	}
}
