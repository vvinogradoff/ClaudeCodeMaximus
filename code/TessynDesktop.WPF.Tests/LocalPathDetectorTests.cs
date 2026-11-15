using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.26.5: absolute local paths in assistant text become chips.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class LocalPathDetectorTests
{
	private readonly LocalPathDetector _detector = new();

	[Theory]
	[InlineData(@"I wrote the plan to C:\Projects\Tessyn\docs\plan.md for review.", @"C:\Projects\Tessyn\docs\plan.md")]
	[InlineData(@"See C:\a\b.txt.", @"C:\a\b.txt")]
	[InlineData(@"(C:\a\b.png)", @"C:\a\b.png")]
	[InlineData(@"Saved `C:\Users\me\My Documents\report final.pdf` there", @"C:\Users\me\My Documents\report final.pdf")]
	[InlineData("open \"D:\\data\\clip one.mp4\" now", @"D:\data\clip one.mp4")]
	[InlineData(@"Share: \\server\share\folder\notes.md", @"\\server\share\folder\notes.md")]
	[InlineData(@"- **C:\out\result.json**", @"C:\out\result.json")]
	public void FindsPaths(string text, string expected) => Assert.Equal([expected], _detector.Detect(text));

	[Theory]
	[InlineData(@"the folder C:\Projects\Tessyn is big")]
	[InlineData(@"relative docs\plan.md")]
	[InlineData("https://example.com/C:/a/b.png")]
	[InlineData("/usr/local/bin/tool.sh")]
	[InlineData("no paths here")]
	[InlineData("")]
	public void NotAFilePath_NoChip(string text) => Assert.Empty(_detector.Detect(text));

	[Fact]
	public void DuplicatesAreMergedAndTheCountIsCapped()
	{
		var text = @"C:\a\one.md again C:\a\one.md " + string.Join(" ", Enumerable.Range(0, 20).Select(i => $@"C:\a\file{i}.txt"));

		var found = _detector.Detect(text);

		Assert.Equal(Constants.Limits.MaxPathChipsPerEntry, found.Count);
		Assert.Equal(found.Count, found.Distinct().Count());
		Assert.Equal(@"C:\a\one.md", found[0]);
	}

	[Theory]
	[InlineData(@"C:\a\b.md", true)]
	[InlineData(@"\\srv\share\b.md", true)]
	[InlineData(@"C:\a\b", false)]
	[InlineData(@"C:\a\b.", false)]
	[InlineData(@"a\b.md", false)]
	[InlineData("C:\\a\\b.md\n", false)]
	[InlineData(null, false)]
	public void IsPathShaped_IsTheSameTestTheHostAppliesToAClickedPath(string? path, bool expected) => Assert.Equal(expected, _detector.IsPathShaped(path));

	[Fact]
	public void TheRendererAttachesPathsAndMediaToAssistantEntriesOnly()
	{
		var policy = new MediaPreviewPolicy();
		var renderer = new MarkdigEntryRenderer(new UrlPolicy(), new MediaLinkDetector(policy), _detector);
		var text = @"Plan at C:\p\plan.md and chart https://example.com/chart.png";

		var assistant = renderer.Render(Entry("assistant", text));
		var user = renderer.Render(Entry("user", text));

		Assert.Equal([@"C:\p\plan.md"], assistant.Paths);
		Assert.Equal(["https://example.com/chart.png"], assistant.Media);
		Assert.Null(user.Paths);
		Assert.Null(user.Media);
		Assert.DoesNotContain("<img", assistant.Html, StringComparison.OrdinalIgnoreCase);
	}

	private static SessionEntryDto Entry(string role, string content) => new() { Index = 1, Role = role, Content = content, Timestamp = DateTimeOffset.UnixEpoch };
}
