using System.Collections.Generic;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>TC.TG.01, TC.TG.02, TC.TG.03 — <see cref="SessionTagParser"/> (FR.24.1, FR.24.2, FR.24.4, FR.24.10).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagParserTests
{
	private readonly SessionTagParser _parser = new();

	private static readonly Dictionary<string, string> HseMap = new() { ["HSE"] = "hardstop" };

	private SessionTagParseResultModel Parse(string? entry) => _parser.Parse(entry, HseMap);

	[Theory]
	[InlineData("https://linear.app/hardstop/issue/HSE-230", SessionTagKind.LinearIssue, "HSE-230", "HSE-230", "https://linear.app/hardstop/issue/HSE-230")]
	[InlineData("https://linear.app/hardstop/issue/HSE-230/fix-the-thing?x=1", SessionTagKind.LinearIssue, "HSE-230", "HSE-230", "https://linear.app/hardstop/issue/HSE-230")]
	[InlineData("HSE-230", SessionTagKind.LinearIssue, "HSE-230", "HSE-230", "https://linear.app/hardstop/issue/HSE-230")]
	[InlineData("hse-230", SessionTagKind.LinearIssue, "HSE-230", "HSE-230", "https://linear.app/hardstop/issue/HSE-230")]
	[InlineData("https://github.com/tryhardstop/windows-app/pull/215", SessionTagKind.GitHubPullRequest, "tryhardstop/windows-app#215", "windows-app PR #215", "https://github.com/tryhardstop/windows-app/pull/215")]
	[InlineData("https://github.com/tryhardstop/windows-app/pull/215/files?w=1#diff", SessionTagKind.GitHubPullRequest, "tryhardstop/windows-app#215", "windows-app PR #215", "https://github.com/tryhardstop/windows-app/pull/215")]
	[InlineData("tryhardstop/windows-app#215", SessionTagKind.GitHubPullRequest, "tryhardstop/windows-app#215", "windows-app PR #215", "https://github.com/tryhardstop/windows-app/pull/215")]
	[InlineData("https://github.com/tryhardstop/windows-app", SessionTagKind.GitHubRepository, "tryhardstop/windows-app", "windows-app", "https://github.com/tryhardstop/windows-app")]
	[InlineData("https://github.com/tryhardstop/windows-app.git", SessionTagKind.GitHubRepository, "tryhardstop/windows-app", "windows-app", "https://github.com/tryhardstop/windows-app")]
	[InlineData("https://github.com/tryhardstop/windows-app/", SessionTagKind.GitHubRepository, "tryhardstop/windows-app", "windows-app", "https://github.com/tryhardstop/windows-app")]
	[InlineData("tryhardstop/windows-app", SessionTagKind.GitHubRepository, "tryhardstop/windows-app", "windows-app", "https://github.com/tryhardstop/windows-app")]
	[InlineData("https://LINEAR.APP/hardstop/issue/HSE-230", SessionTagKind.LinearIssue, "HSE-230", "HSE-230", "https://linear.app/hardstop/issue/HSE-230")]   // TC.TG.01-4
	public void Parse_AcceptedForms(string input, SessionTagKind kind, string key, string label, string url)   // TC.TG.01
	{
		var result = Parse(input);

		Assert.Equal(SessionTagParseOutcome.Valid, result.Outcome);
		Assert.Equal(kind, result.Tag!.Kind);
		Assert.Equal(key, result.Tag.Key);
		Assert.Equal(label, result.Tag.Label);
		Assert.Equal(url, result.Tag.Url);
	}

	[Fact]
	public void Parse_TrimsTheEntry()   // TC.TG.01-1
	{
		Assert.Equal("HSE-230", Parse("  HSE-230  ").Tag!.Key);
	}

	[Fact]
	public void Parse_KeepsOwnerAndRepoCaseForLabelAndUrl()   // TC.TG.01-2
	{
		var tag = Parse("TryHardStop/Windows-App#215").Tag!;

		Assert.Equal("Windows-App PR #215", tag.Label);
		Assert.Equal("https://github.com/TryHardStop/Windows-App/pull/215", tag.Url);
		Assert.Equal(Parse("tryhardstop/windows-app#215").Tag!.Key, tag.Key, ignoreCase: true);
	}

	[Fact]
	public void Parse_FullLinearUrlReportsTheLearnedPair_ShortKeyDoesNot()   // TC.TG.01-3
	{
		var full = Parse("https://linear.app/hardstop/issue/HSE-230");
		Assert.Equal("HSE", full.LearnedTeam);
		Assert.Equal("hardstop", full.LearnedWorkspace);

		var learnedFromUnknown = _parser.Parse("https://linear.app/acme/issue/ABC-1", new Dictionary<string, string>());
		Assert.Equal(("ABC", "acme"), (learnedFromUnknown.LearnedTeam, learnedFromUnknown.LearnedWorkspace));

		Assert.Null(Parse("HSE-230").LearnedTeam);
	}

	[Theory]
	[InlineData("foo")]
	[InlineData("HSE")]
	[InlineData("HSE-")]
	[InlineData("#215")]
	[InlineData("windows-app#215")]
	[InlineData("http://example.com")]
	[InlineData("https://github.com")]
	[InlineData("https://github.com/onlyowner")]
	[InlineData("https://linear.app/hardstop")]
	[InlineData("https://gitlab.com/o/r")]
	[InlineData("https://github.com.evil.io/o/r")]   // TC.TG.02-4
	[InlineData("https://evil.io/github.com")]
	[InlineData("https://evil.io/github.com/o/r")]
	[InlineData("o/r#0")]                            // TC.TG.02-5
	[InlineData("o/r#abc")]
	public void Parse_InvalidInput(string input)   // TC.TG.02-1, 02-4, 02-5
	{
		Assert.Equal(SessionTagParseOutcome.Invalid, Parse(input).Outcome);
	}

	[Fact]
	public void Parse_ValidPullRequestNumber_IsAccepted()   // TC.TG.02-5
	{
		Assert.Equal(SessionTagParseOutcome.Valid, Parse("o/r#215").Outcome);
	}

	[Fact]
	public void Parse_UnknownTeam_IsDistinctFromInvalid()   // TC.TG.02-2
	{
		var result = Parse("ABC-123");

		Assert.Equal(SessionTagParseOutcome.UnknownTeam, result.Outcome);
		Assert.Equal("ABC", result.UnknownTeam);
		Assert.Equal("ABC-123", result.UnknownIssueKey);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData(null)]
	public void Parse_BlankEntry_IsIgnored(string? input)   // TC.TG.02-3
	{
		Assert.Equal(SessionTagParseOutcome.Empty, Parse(input).Outcome);
	}

	// ── TC.TG.03 name token ───────────────────────────────────────────────────

	[Theory]
	[InlineData("HSE-150 Android backend", "HSE-150")]
	[InlineData("W14 HSE-165 rebase", "HSE-165")]
	[InlineData("(HSE-150)", "HSE-150")]
	[InlineData("[HSE-150]", "HSE-150")]
	[InlineData("HSE-150:", "HSE-150")]
	[InlineData("HSE-150.", "HSE-150")]
	[InlineData("H-1", "H-1")]
	public void FindNameKeys_Matches(string name, string expected)   // TC.TG.03-1, 03-3 (H-1)
	{
		Assert.Equal([expected], _parser.FindNameKeys(name));
	}

	[Fact]
	public void FindNameKeys_Several_InOrderOfAppearance()   // TC.TG.03-2
	{
		Assert.Equal(["HSE-150", "ABC-9", "HSE-151"], _parser.FindNameKeys("HSE-150 and ABC-9, HSE-151"));
	}

	[Theory]
	[InlineData("xHSE-150")]
	[InlineData("HSE-1234567")]
	[InlineData("HSE-150a")]
	[InlineData("hse-150")]
	[InlineData("1HSE-5")]
	[InlineData("ABCDEFGHIJK-1")]
	[InlineData("")]
	public void FindNameKeys_NoMatch(string name)   // TC.TG.03-3
	{
		Assert.Empty(_parser.FindNameKeys(name));
	}

	[Fact]
	public void FindNameKeys_RepeatedKey_YieldsOneToken()   // TC.TG.03-4
	{
		Assert.Equal(["HSE-150"], _parser.FindNameKeys("HSE-150 HSE-150"));
	}
}
