namespace TessynDesktop.WPF.Rendering;

/// <summary>Turns untrusted markdown into sanitized HTML (raw HTML disabled, images inert, http(s) links only). Pure and thread-safe.</summary>
/// <remarks>Created by Claude</remarks>
public interface IMarkdownRenderer
{
	string RenderMarkdown(string markdown);
}
