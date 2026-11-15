namespace TessynDesktop.WPF.Services;

/// <summary>
/// The preview pane's content area: one sandboxed page (WebView2 in the app, a recording fake in tests). Text is always shown as
/// text; markdown arrives as sanitized HTML from <c>Rendering.IMarkdownRenderer</c>; media are addressed by their URL in the preview
/// cache. All members are called on the UI thread.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IPreviewSurface
{
	/// <summary>Shows plain text in a monospace font (also svg, html and scripts; they are never rendered or run).</summary>
	void ShowText(string text);

	/// <summary>Shows markdown that was rendered to HTML with raw HTML disabled.</summary>
	void ShowMarkdown(string html);

	/// <summary>Shows an image served from the preview cache.</summary>
	void ShowImage(string url);

	/// <summary>Shows a video served from the preview cache.</summary>
	void ShowVideo(string url);

	/// <summary>Empties the page.</summary>
	void Clear();

	/// <summary>The user clicked a link in rendered markdown (argument: the raw href; validated by the receiver).</summary>
	event EventHandler<string>? LinkClicked;

	/// <summary>The page showed the content, or could not.</summary>
	event EventHandler<PreviewRenderedEventArgs>? Rendered;
}
