using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>How a previewed file is shown, decided from its name and MIME type.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FileKindClassifierTests
{
	private readonly FileKindClassifier _classifier = new();

	[Theory]
	[InlineData("plan.md", "text/markdown", PreviewKind.Markdown)]
	[InlineData("README.MARKDOWN", null, PreviewKind.Markdown)]
	[InlineData("a.txt", "text/plain", PreviewKind.Text)]
	[InlineData("Program.cs", "text/plain", PreviewKind.Text)]
	[InlineData("data.json", "application/json", PreviewKind.Text)]
	[InlineData("pic.png", "image/png", PreviewKind.Image)]
	[InlineData("pic.JPG", null, PreviewKind.Image)]
	[InlineData("clip.mp4", "video/mp4", PreviewKind.Video)]
	[InlineData("doc.pdf", "application/pdf", PreviewKind.Pdf)]
	public void KnownTypes(string name, string? mime, PreviewKind expected) => Assert.Equal(expected, _classifier.Classify(name, mime));

	[Theory]
	[InlineData("drawing.svg", "image/svg+xml")]
	[InlineData("page.html.txt", "text/plain")]
	[InlineData("run.ps1", "text/plain")]
	[InlineData("run.bat", null)]
	[InlineData("run.cmd", null)]
	[InlineData("noext", "image/svg+xml")]
	public void SvgHtmlAndScripts_AreTextNeverRendered(string name, string? mime) => Assert.Equal(PreviewKind.Text, _classifier.Classify(name, mime));

	[Theory]
	[InlineData("page.html", "text/html")]
	[InlineData("page.htm", null)]
	[InlineData("PAGE.HTML", null)]
	[InlineData("Index.HtM", "text/plain")]
	[InlineData("noext", "text/html")]
	[InlineData(null, "TEXT/HTML; charset=utf-8")]
	public void Html_IsItsOwnKind_ByExtensionAndByMime(string? name, string? mime) => Assert.Equal(PreviewKind.Html, _classifier.Classify(name, mime));

	[Fact]
	public void Html_HasTheTextLimit() => Assert.Equal(_classifier.LimitOf(PreviewKind.Text), _classifier.LimitOf(PreviewKind.Html));

	[Theory]
	[InlineData("setup.exe", "application/octet-stream")]
	[InlineData("archive.zip", null)]
	[InlineData("noext", null)]
	[InlineData(null, null)]
	public void UnknownTypes_HaveNoPreview(string? name, string? mime) => Assert.Equal(PreviewKind.Unsupported, _classifier.Classify(name, mime));

	[Fact]
	public void MimeTypeIsUsedWhenTheNameSaysNothing()
	{
		Assert.Equal(PreviewKind.Image, _classifier.Classify("blob", "image/webp"));
		Assert.Equal(PreviewKind.Video, _classifier.Classify("blob", "video/webm"));
		Assert.Equal(PreviewKind.Markdown, _classifier.Classify("blob", "text/markdown"));
	}

	[Fact]
	public void LimitsFollowTheKind()
	{
		Assert.Equal(Constants.Limits.MaxPreviewTextBytes, _classifier.LimitOf(PreviewKind.Markdown));
		Assert.Equal(Constants.Limits.MaxPreviewTextBytes, _classifier.LimitOf(PreviewKind.Text));
		Assert.Equal(Constants.Limits.MaxPreviewImageBytes, _classifier.LimitOf(PreviewKind.Image));
		Assert.Equal(Constants.Limits.MaxPreviewVideoBytes, _classifier.LimitOf(PreviewKind.Video));
		Assert.Equal(Constants.Limits.MaxPreviewPdfBytes, _classifier.LimitOf(PreviewKind.Pdf));
		Assert.Equal(0, _classifier.LimitOf(PreviewKind.Unsupported));
	}
}
