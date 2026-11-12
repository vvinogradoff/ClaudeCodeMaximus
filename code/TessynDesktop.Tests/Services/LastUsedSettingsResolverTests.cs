using System.Collections.Generic;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;
using static TessynDesktop.Tests.Services.RemoteTestHelpers;

namespace TessynDesktop.Tests.Services;

/// <summary>The "last used" settings of session.get mirror what the desktop restores when a session is opened.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class LastUsedSettingsResolverTests
{
	private const string DefaultLabel = "[Sys] me@example.com";

	private static readonly ClaudeModelInfo[] KnownModels =
	[
		new("claude-opus-5-5", "opus", "Opus 5.5"),
		new("claude-sonnet-5-5", "sonnet", "Sonnet 5.5"),
	];

	private static readonly ClaudeProfileModel[] Profiles =
	[
		new() { ProfileId = "p1", DisplayName = "Work" },
		new() { ProfileId = "p2", DisplayName = "Home" },
	];

	private static Models.Remote.LastUsedSettingsDto Resolve(params SessionEntryModel[] entries) =>
		LastUsedSettingsResolver.Resolve(entries, KnownModels, Profiles, DefaultLabel);

	[Fact]
	public void NoEntries_AreAllDefault()
	{
		var last = Resolve();

		Assert.Equal("default", last.ModelId);
		Assert.Equal("default", last.Effort);
		Assert.Equal("default", last.ProfileId);
		Assert.Equal(DefaultLabel, last.ProfileName);
	}

	[Fact]
	public void ModelAndEffort_ComeFromTheLastAssistantEntryThatRecordedAModel()
	{
		var last = Resolve(
			Assistant("a", 1, "claude-sonnet-5-5", "low"),
			Assistant("b", 2, "claude-opus-5-5", "high"),
			Assistant("no stamp", 3));

		Assert.Equal("claude-opus-5-5", last.ModelId);
		Assert.Equal("high", last.Effort);
	}

	[Fact]
	public void UnknownModel_AndUnknownEffort_FallBackToDefault()
	{
		var last = Resolve(Assistant("a", 1, "claude-retired", "turbo"));

		Assert.Equal("default", last.ModelId);
		Assert.Equal("default", last.Effort);
	}

	[Fact]
	public void DefaultStamps_StayDefault()
	{
		var last = Resolve(Assistant("a", 1, "default", "default"));

		Assert.Equal("default", last.ModelId);
		Assert.Equal("default", last.Effort);
	}

	[Fact]
	public void Effort_IsTakenFromTheSameEntryAsTheModel_NotFromALaterOneWithoutModel()
	{
		var last = Resolve(
			Assistant("a", 1, "claude-opus-5-5", "max"),
			Entry(Constants.SessionFile.RoleAssistant, "b", 2, effort: "low"));

		Assert.Equal("max", last.Effort);
	}

	[Fact]
	public void Profile_ComesFromTheLastUserEntryThatRecordedOne_MatchedCaseInsensitively()
	{
		var last = Resolve(User("a", 1, "Home"), User("b", 2, "work"), User("c", 3));

		Assert.Equal("p1", last.ProfileId);
		Assert.Equal("Work", last.ProfileName);
	}

	[Theory]
	[InlineData("[Sys] someone@else.com")]
	[InlineData("Deleted profile")]
	public void SystemLabelAndUnknownProfile_FallBackToDefault(string recorded)
	{
		var last = Resolve(User("a", 1, recorded));

		Assert.Equal("default", last.ProfileId);
		Assert.Equal(DefaultLabel, last.ProfileName);
	}

	[Fact]
	public void OtherRoles_AreIgnored()
	{
		var last = Resolve(
			Entry(Constants.SessionFile.RoleSystem, "s", 1, profile: "Work", model: "claude-opus-5-5", effort: "high"),
			Entry(Constants.SessionFile.RoleUser, "u", 2, model: "claude-opus-5-5"),
			Entry(Constants.SessionFile.RoleAssistant, "a", 3, profile: "Work"));

		Assert.Equal("default", last.ModelId);
		Assert.Equal("default", last.ProfileId);
	}
}
