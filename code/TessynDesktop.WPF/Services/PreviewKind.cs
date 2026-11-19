namespace TessynDesktop.WPF.Services;

/// <summary>How the preview pane shows a file.</summary>
/// <remarks>Created by Claude</remarks>
public enum PreviewKind
{
	/// <summary>No preview; the file is not read.</summary>
	Unsupported,

	/// <summary>Markdown, rendered with raw HTML disabled; the source can be shown instead.</summary>
	Markdown,

	/// <summary>An html page, rendered static in a sandboxed frame (no scripts, no network); the source can be shown instead (FR.26.8).</summary>
	Html,

	/// <summary>Text and code in a monospace font. Also svg and scripts: shown as text, never rendered or run.</summary>
	Text,

	Image,

	Video,

	/// <summary>Not shown; only "Open in default app" is offered.</summary>
	Pdf,
}
