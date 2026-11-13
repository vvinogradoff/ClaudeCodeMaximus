using System.Collections.Generic;
using TessynDesktop.Models;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary><see cref="TessynDesktop.Services.ISessionTagService.Parse"/> — read-only parsing for the tag search tools (FR.24.15).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagServiceParseTests
{
	[Fact]
	public void Parse_ResolvesShortKeysWithTheLoadedWorkspaceMap_AndUrlsWithoutIt()
	{
		using var env = new TagTestEnv();
		env.Store.SaveWorkspaces(new Dictionary<string, string> { ["HSE"] = "hardstop" });

		var known = env.Service.Parse("hse-254");
		var url   = env.Service.Parse(" https://github.com/o/r/pull/5 ");

		Assert.Equal(SessionTagParseOutcome.Valid, known.Outcome);
		Assert.Equal("HSE-254", known.Tag!.Key);
		Assert.Equal("https://linear.app/hardstop/issue/HSE-254", known.Tag.Url);
		Assert.Equal("o/r#5", url.Tag!.Key);
	}

	[Fact]
	public void Parse_UnknownTeamAndInvalidEntries_ReportTheirOutcome()
	{
		using var env = new TagTestEnv();

		var unknown = env.Service.Parse("ABC-12");
		var invalid = env.Service.Parse("xyz");
		var blank   = env.Service.Parse("  ");

		Assert.Equal(SessionTagParseOutcome.UnknownTeam, unknown.Outcome);
		Assert.Equal("ABC-12", unknown.UnknownIssueKey);
		Assert.Equal(SessionTagParseOutcome.Invalid, invalid.Outcome);
		Assert.Equal(SessionTagParseOutcome.Empty, blank.Outcome);
	}

	[Fact]
	public void Parse_NeverLearnsOrPersists_EvenForAFullLinearUrl()
	{
		using var env = new TagTestEnv();
		var raised = 0;
		env.Service.SessionTagsChanged += (_, _) => raised++;

		var result = env.Service.Parse("https://linear.app/acme/issue/ABC-12");
		var after  = env.Service.Parse("ABC-12");

		Assert.Equal("acme", result.LearnedWorkspace);
		Assert.Equal(SessionTagParseOutcome.UnknownTeam, after.Outcome);
		Assert.Empty(env.Store.LoadWorkspaces());
		Assert.Equal(0, raised);
	}
}
