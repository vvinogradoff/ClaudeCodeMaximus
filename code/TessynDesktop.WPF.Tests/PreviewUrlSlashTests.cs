using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.29.3: a URL pattern ending with <c>/*</c> also matches the address without that last slash.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewUrlSlashTests
{
	private readonly PreviewGroupMatcher _matcher = new();

	private static PreviewGroupModel Group(string id, string patterns) => new() { Id = id, Name = id, Patterns = patterns };

	[Theory]
	[InlineData("https://github.com/tryhardstop/windows-app")]
	[InlineData("https://github.com/tryhardstop/windows-app/")]
	[InlineData("https://github.com/tryhardstop/windows-app/pull/3")]
	[InlineData("HTTPS://GitHub.com/TryHardStop/Windows-App")]
	public void ARepositoryAddress_MatchesItsSlashStarPattern(string url)
	{
		var match = _matcher.MatchUrl([Group("repo", "https://github.com/tryhardstop/windows-app/*")], url);

		Assert.Equal("repo", match?.Group.Id);
	}

	[Fact]
	public void TheSlashlessMatch_KeepsThePatternsSpecificity()
	{
		var groups = new[] { Group("web", "https://*"), Group("repo", "https://github.com/tryhardstop/windows-app/*") };

		Assert.Equal("repo", _matcher.MatchUrl(groups, "https://github.com/tryhardstop/windows-app")?.Group.Id);
		Assert.Equal("web", _matcher.MatchUrl(groups, "https://github.com/tryhardstop/other")?.Group.Id);
		Assert.Equal("https://github.com/tryhardstop/windows-app/*".Count(c => c != '*'), _matcher.MatchUrl(groups, "https://github.com/tryhardstop/windows-app")?.Pattern.Specificity);
	}

	[Fact]
	public void ASiblingRepository_DoesNotMatch()
	{
		Assert.Null(_matcher.MatchUrl([Group("repo", "https://github.com/tryhardstop/windows-app/*")], "https://github.com/tryhardstop/windows-app2"));
	}

	[Fact]
	public void HttpsStar_MatchesEveryHttpsUrl_ButNotHttp()
	{
		var groups = new[] { Group("web", "https://*") };

		Assert.NotNull(_matcher.MatchUrl(groups, "https://example.com"));
		Assert.NotNull(_matcher.MatchUrl(groups, "https://example.com/a/b?c=1#d"));
		Assert.Null(_matcher.MatchUrl(groups, "http://example.com"));
	}

	[Theory]
	[InlineData("https:/")]
	[InlineData("https:")]
	[InlineData("https://")]
	[InlineData("")]
	public void HttpsStar_DoesNotMatchAFragmentOfTheScheme(string url)
	{
		Assert.Null(_matcher.MatchUrl([Group("web", "https://*")], url));
	}
}
