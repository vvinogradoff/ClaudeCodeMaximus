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
	[InlineData("https://example.com/C:/a/b.png")]
	[InlineData("/usr/local/bin/tool.sh")]
	[InlineData("no paths here")]
	[InlineData("")]
	public void NotAFilePath_NoChip(string text) => Assert.Empty(_detector.Detect(text));

	[Theory]
	[InlineData("see docs/features/preview-windows.md for it", "docs/features/preview-windows.md")]
	[InlineData(@"open code\App.xaml.cs now", @"code\App.xaml.cs")]
	[InlineData("run ./src/a.ts.", "./src/a.ts")]
	[InlineData(@"in ..\x\y.cs", @"..\x\y.cs")]
	[InlineData("the .github/workflows/ci.yml file", ".github/workflows/ci.yml")]
	[InlineData("see (docs/a.md)", "docs/a.md")]
	[InlineData("**docs/a.md**", "docs/a.md")]
	[InlineData("saved `docs/my notes/final plan.md` there", "docs/my notes/final plan.md")]
	[InlineData("open \"docs/my notes/plan.md\" now", "docs/my notes/plan.md")]
	[InlineData("see `README.md`", "README.md")]
	public void FindsRelativePaths(string text, string expected) => Assert.Equal([expected], _detector.Detect(text));

	[Theory]
	[InlineData("https://x/a.md")]
	[InlineData("https://example.com/docs/a.md")]
	[InlineData("example.com/page.html")]
	[InlineData("www.example.org/a/b.html")]
	[InlineData("v2.0 and 1.2.3 and 1.2.3/x")]
	[InlineData("docs/plan")]
	[InlineData("README.md is here")]
	[InlineData("call `string.Empty` or `Console.WriteLine`")]
	[InlineData("/usr/local/bin/tool.sh")]
	[InlineData("it's fine, don't worry")]
	public void NotARelativePath_NoMark(string text) => Assert.Empty(_detector.Detect(text));

	[Fact]
	public void AnAbsolutePathIsNotAlsoFoundAsARelativeOne()
	{
		Assert.Equal([@"C:\a\b\c.md"], _detector.Detect(@"see C:\a\b\c.md"));
		Assert.Equal([@"\\srv\share\b\c.md"], _detector.Detect(@"see \\srv\share\b\c.md"));
	}

	[Fact]
	public void RelativePathsFollowTheAbsoluteOnesAndAreMergedWithoutDuplicates()
	{
		var found = _detector.Detect(@"docs/a.md then C:\p\q.md then docs/a.md and `README.md`");

		Assert.Equal([@"C:\p\q.md", "docs/a.md", "README.md"], found);
	}

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
	[InlineData(@"a\b.md", true)]
	[InlineData("docs/a.md", true)]
	[InlineData("README.md", true)]
	[InlineData("README", false)]
	[InlineData("example.com/page.html", false)]
	[InlineData("1.2.3/x", false)]
	[InlineData("/a/b.md", false)]
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
