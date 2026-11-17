using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.26.5: the local paths of an assistant entry stay inline in the text (marked, not moved into a chip row); image paths get a preview slot.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class InlinePathMarkerTests
{
	private readonly MarkdigEntryRenderer _renderer = new(new UrlPolicy(), paths: new LocalPathDetector());

	private TranscriptEntryView Render(string content, string role = "assistant") =>
		_renderer.Render(new SessionEntryDto { Index = 1, Role = role, Content = content, Timestamp = DateTimeOffset.UnixEpoch });

	[Fact]
	public void ABarePath_StaysWhereItIsInTheSentence_AsAMarkedSpan()
	{
		var view = Render(@"The plan is at C:\work\plan.md and then we go on.");

		Assert.Equal([@"C:\work\plan.md"], view.Paths);
		Assert.Contains(@"<p>The plan is at <span class=""pth"" data-j=""0"">C:\work\plan.md</span> and then we go on.</p>", view.Html);
		Assert.DoesNotContain("pslot", view.Html);      // not an image: no slot
	}

	[Fact]
	public void APathInBackticks_IsMarkedInsideTheCodeSpan()
	{
		var view = Render(@"Open `C:\my docs\a b.txt` please");

		Assert.Contains(@"<code><span class=""pth"" data-j=""0"">C:\my docs\a b.txt</span></code>", view.Html);
	}

	[Fact]
	public void AnImagePath_GetsAnEmptySlotRightAfterIt_ForTheFirstOccurrenceOnly()
	{
		var view = Render(@"Shot: C:\s\a.png and again C:\s\a.png.");

		Assert.Equal(1, Count(view.Html, @"<span class=""pslot"" data-j=""0""></span>"));
		Assert.Equal(2, Count(view.Html, @"data-j=""0"">C:\s\a.png</span>"));
		Assert.Contains(@"<span class=""pth"" data-j=""0"">C:\s\a.png</span><span class=""pslot"" data-j=""0""></span> and again", view.Html);
	}

	[Theory]
	[InlineData(@"C:\s\a.png")]
	[InlineData(@"C:\s\a.JPG")]
	[InlineData(@"C:\s\a.jpeg")]
	[InlineData(@"C:\s\a.gif")]
	[InlineData(@"C:\s\a.bmp")]
	[InlineData(@"C:\s\a.webp")]
	public void EveryInlineImageType_GetsASlot(string path) => Assert.Contains("pslot", Render("see " + path + " now").Html);

	[Theory]
	[InlineData(@"C:\s\a.svg")]          // a text type here: never rendered, the preview pane shows it as text
	[InlineData(@"C:\s\a.md")]
	[InlineData(@"C:\s\a.csv")]
	[InlineData(@"C:\s\a.txt")]
	public void OtherFiles_GetThePathButNoSlot(string path)
	{
		var html = Render("see " + path + " now").Html;

		Assert.Contains("class=\"pth\"", html);
		Assert.DoesNotContain("pslot", html);
	}

	[Fact]
	public void APathInACodeBlock_IsMarkedToo()
	{
		var view = Render("```\ntype C:\\logs\\run.txt\n```");

		Assert.Contains(@"<span class=""pth"" data-j=""0"">C:\logs\run.txt</span>", view.Html);
	}

	[Fact]
	public void AUncPath_IsMarkedEvenThoughMarkdownDropsOneLeadingBackslash()
	{
		var view = Render(@"Look at \\srv\share\pic.png now");

		Assert.Equal([@"\\srv\share\pic.png"], view.Paths);
		Assert.Contains("class=\"pth\"", view.Html);
		Assert.Contains("pslot", view.Html);
	}

	[Fact]
	public void TwoPaths_GetTheirOwnIndexes_AndTheLongerOneWins()
	{
		var view = Render(@"Compare C:\a\x.png.bak with C:\a\x.png please");

		Assert.Equal(2, view.Paths!.Count);
		Assert.Contains(@"data-j=""0"">C:\a\x.png.bak</span>", view.Html);
		Assert.Contains(@"data-j=""1"">C:\a\x.png</span>", view.Html);
	}

	[Fact]
	public void APathInsideALink_IsLeftAlone()
	{
		var view = Render(@"[C:\work\plan.md](https://example.com/x) and C:\work\plan.md");

		Assert.Equal(1, Count(view.Html, "class=\"pth\""));
		Assert.Contains("<a href=\"https://example.com/x\">C:\\work\\plan.md</a>", view.Html);
	}

	[Fact]
	public void TheOutput_AddsNoElementThatCanLoadAResource_AndEscapesTheText()
	{
		var view = Render("see C:\\x\\a&b.png and <img src=x onerror=alert(1)> done");

		Assert.DoesNotContain("<img", view.Html);
		Assert.DoesNotContain("<script", view.Html, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void OnlyAssistantEntries_GetPaths()
	{
		var view = Render(@"I wrote C:\work\plan.md", role: "user");

		Assert.Empty(view.Paths ?? []);
		Assert.DoesNotContain("pth", view.Html);
	}

	[Fact]
	public void WithoutPaths_TheHtmlIsExactlyTheMarkdownRendering()
	{
		const string markdown = "plain **text** only";

		Assert.Equal(_renderer.RenderMarkdown(markdown), Render(markdown).Html);
	}

	private static int Count(string text, string part)
	{
		var n = 0;
		for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + 1, StringComparison.Ordinal))
			n++;

		return n;
	}
}
