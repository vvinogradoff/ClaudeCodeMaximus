using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>Teal and the label of an automated input (FR.2.6).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class InputSourceDisplayTests
{
	private readonly InputSourceDisplay _display = new();

	[Theory]
	[InlineData(EntrySources.Scheduled, true, "SCHEDULED")]
	[InlineData(EntrySources.Orchestrated, true, "SUPERVISOR")]
	[InlineData(EntrySources.Telegram, false, "You")]
	[InlineData(EntrySources.Web, false, "You")]
	[InlineData(null, false, "You")]
	public void SourceDecidesColourAndLabel(string? source, bool automated, string label)
	{
		Assert.Equal(automated, _display.IsAutomated(source));
		Assert.Equal(label, _display.Label(source));
	}

	[Fact]
	public void ALegacyScheduledPrefix_ResolvedOnRead_IsAutomated()
	{
		var source = EntrySources.Resolve(Constants.SessionFile.RoleUser, null, "[Scheduled] run the nightly check");

		Assert.True(_display.IsAutomated(source));
		Assert.Equal("SCHEDULED", _display.Label(source));
	}

	[Fact]
	public void ARecordedSource_IsNotOverriddenByThePrefix()
	{
		var source = EntrySources.Resolve(Constants.SessionFile.RoleUser, EntrySources.Orchestrated, "[Scheduled] x");

		Assert.Equal("SUPERVISOR", _display.Label(source));
	}
}
