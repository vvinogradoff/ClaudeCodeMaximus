using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Records what the preview pane shows and lets tests raise page events.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakePreviewSurface : IPreviewSurface
{
	public List<string> Calls { get; } = [];

	public string? LastText { get; private set; }

	public string? LastHtml { get; private set; }

	public string? LastUrl { get; private set; }

	public event EventHandler<string>? LinkClicked;

	public event EventHandler<PreviewRenderedEventArgs>? Rendered;

	public void ShowText(string text)
	{
		LastText = text;
		Calls.Add("text");
	}

	public void ShowMarkdown(string html)
	{
		LastHtml = html;
		Calls.Add("markdown");
	}

	public void ShowImage(string url)
	{
		LastUrl = url;
		Calls.Add("image");
	}

	public void ShowVideo(string url)
	{
		LastUrl = url;
		Calls.Add("video");
	}

	public void Clear() => Calls.Add("clear");

	public void RaiseLinkClicked(string href) => LinkClicked?.Invoke(this, href);

	public void RaiseRendered(string kind, bool ok) => Rendered?.Invoke(this, new PreviewRenderedEventArgs(kind, ok));
}
