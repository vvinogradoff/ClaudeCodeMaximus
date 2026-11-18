using System.Diagnostics;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Tests;

/// <summary>Pattern parsing and group matching of the preview windows (FR.29.2, FR.29.3).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewGroupMatcherTests
{
	private readonly PreviewGroupMatcher _matcher = new();

	private static List<PreviewGroupModel> Defaults() =>
		Constants.Previews.DefaultGroups.Select(d => new PreviewGroupModel { Id = d.Id, Name = d.Name, Patterns = d.Patterns }).ToList();

	private static PreviewGroupModel Group(string id, string patterns) => new() { Id = id, Name = id, Patterns = patterns };

	[Fact]
	public void Parse_SplitsOnWhitespaceAndCommas_AndNormalizesExtensions()
	{
		var result = _matcher.Parse("  md,.TXT  h\tcpp ,, https://a.com/* ");

		Assert.Empty(result.Invalid);
		Assert.Equal(["md", "txt", "h", "cpp"], result.Patterns.Where(p => p.Kind == PreviewPatternKind.Extension).Select(p => p.Text));
		var url = Assert.Single(result.Patterns, p => p.Kind == PreviewPatternKind.Url);
		Assert.Equal("https://a.com/*", url.Text);
		Assert.Equal(14, url.Specificity);
	}

	[Theory]
	[InlineData("ftp://x")]
	[InlineData(@"C:\x")]
	[InlineData("*.md")]
	[InlineData("md*")]
	[InlineData("https://")]
	[InlineData("http://")]
	[InlineData("m-d")]
	[InlineData("abcdefghijklmnopq")]
	[InlineData(".")]
	public void Parse_RefusesWhatIsNeitherAnExtensionNorAUrlPattern(string token)
	{
		var result = _matcher.Parse(token);

		Assert.Empty(result.Patterns);
		Assert.Equal([token], result.Invalid);
	}

	[Fact]
	public void Parse_NullAndEmpty_AreEmpty()
	{
		Assert.Empty(_matcher.Parse(null).Patterns);
		Assert.Empty(_matcher.Parse("  , ").Invalid);
	}

	[Theory]
	[InlineData(@"C:\x\Plan.MD", "text-code")]
	[InlineData("scan.TIFF", "images")]
	[InlineData("data.csv", "csv")]
	[InlineData("/home/a/b.cpp", "text-code")]
	[InlineData("archive.png.txt", "text-code")]
	[InlineData("pic.jpeg.", "images")]
	public void MatchFile_Defaults(string file, string expectedGroup)
	{
		Assert.Equal(expectedGroup, _matcher.MatchFile(Defaults(), file)?.Group.Id);
	}

	[Theory]
	[InlineData("a.json")]
	[InlineData("README")]
	[InlineData("archive.tar.gz")]
	[InlineData(@"C:\dir.md\file")]
	[InlineData("")]
	[InlineData(null)]
	public void MatchFile_NoGroup(string? file)
	{
		Assert.Null(_matcher.MatchFile(Defaults(), file));
	}

	[Fact]
	public void MatchFile_LastExtensionOnly()
	{
		var groups = new List<PreviewGroupModel> { Group("a", "gz") };

		Assert.Equal("a", _matcher.MatchFile(groups, "archive.tar.gz")?.Group.Id);
		Assert.Null(_matcher.MatchFile(new List<PreviewGroupModel> { Group("a", "tar") }, "archive.tar.gz"));
	}

	[Theory]
	[InlineData("https://github.com/tryhardstop/windows-app/pull/196", "windows-app")]
	[InlineData("https://github.com/tryhardstop/backend-v1/commit/abc", "backend-frontend")]
	[InlineData("https://github.com/tryhardstop/frontend-v1/pull/2", "backend-frontend")]
	[InlineData("https://hardstop.slack.com/archives/C1/p123", "slack")]
	[InlineData("https://linear.app/hardstop/issue/HSE-240/title", "linear")]
	[InlineData("https://example.com", "web")]
	[InlineData("HTTPS://EXAMPLE.COM/Path?x=1#y", "web")]
	[InlineData("https://github.com/other/repo", "web")]
	public void MatchUrl_Defaults(string url, string expectedGroup)
	{
		Assert.Equal(expectedGroup, _matcher.MatchUrl(Defaults(), url)?.Group.Id);
	}

	[Theory]
	[InlineData("http://example.com")]
	[InlineData("ftp://example.com/x")]
	[InlineData("mailto:a@b.c")]
	[InlineData("not a url")]
	[InlineData("")]
	[InlineData(null)]
	public void MatchUrl_NoGroup(string? url)
	{
		Assert.Null(_matcher.MatchUrl(Defaults(), url));
	}

	[Fact]
	public void MatchUrl_IsCaseInsensitive_AndAFileNeverMatchesAUrlPatternOrTheOtherWayRound()
	{
		var groups = new List<PreviewGroupModel> { Group("a", "https://Example.com/Docs/* md") };

		Assert.Equal("a", _matcher.MatchUrl(groups, "https://example.COM/docs/Readme")?.Group.Id);
		Assert.Null(_matcher.MatchUrl(groups, "https://example.com/readme.md"));
		Assert.Null(_matcher.MatchFile(groups, "https://example.com/docs/readme"));
	}

	[Fact]
	public void MatchUrl_StarMatchesSlashQueryAndFragment()
	{
		var groups = new List<PreviewGroupModel> { Group("a", "https://x.com/*/b?c=*#*") };

		Assert.Equal("a", _matcher.MatchUrl(groups, "https://x.com/1/2/b?c=3#frag")?.Group.Id);
	}

	[Fact]
	public void SplittingAGroup_GivesItsOwnWindow()
	{
		var groups = Defaults();
		groups.First(g => g.Id == "text-code").Patterns = "md txt";
		groups.Add(Group("code", "cpp h"));

		Assert.Equal("text-code", _matcher.MatchFile(groups, "a.md")?.Group.Id);
		Assert.Equal("code", _matcher.MatchFile(groups, "a.cpp")?.Group.Id);
		Assert.Equal("code", _matcher.MatchFile(groups, "a.h")?.Group.Id);
	}

	[Fact]
	public void MostSpecificPatternWins_AndATieGoesToTheFirstGroup()
	{
		var groups = new List<PreviewGroupModel> { Group("wide", "https://*"), Group("narrow", "https://a.com/x/*"), Group("narrow2", "https://a.com/x/*") };

		Assert.Equal("narrow", _matcher.MatchUrl(groups, "https://a.com/x/y")?.Group.Id);
		Assert.Equal("wide", _matcher.MatchUrl(groups, "https://a.com/z")?.Group.Id);
	}

	[Fact]
	public void SpecificityBeatsListOrder_ForUrlsAndTieForExtensions()
	{
		var groups = new List<PreviewGroupModel> { Group("first", "md"), Group("second", ".MD") };

		Assert.Equal("first", _matcher.MatchFile(groups, "a.md")?.Group.Id);
	}

	[Fact]
	public void InvalidPatternsOfAGroupAreIgnoredWhenMatching()
	{
		var groups = new List<PreviewGroupModel> { Group("a", "*.md ftp://x md") };

		Assert.Equal("a", _matcher.MatchFile(groups, "x.md")?.Group.Id);
	}

	[Fact]
	public void PathologicalPattern_FinishesFast()
	{
		var groups = new List<PreviewGroupModel> { Group("a", "https://*a*a*a*a*a*a*a*a*a*a*a*a*b") };
		var url = "https://x.com/" + new string('a', 5000);

		var watch = Stopwatch.StartNew();
		var match = _matcher.MatchUrl(groups, url);
		watch.Stop();

		Assert.Null(match);
		Assert.True(watch.ElapsedMilliseconds < 2000, $"took {watch.ElapsedMilliseconds} ms");
	}
}
