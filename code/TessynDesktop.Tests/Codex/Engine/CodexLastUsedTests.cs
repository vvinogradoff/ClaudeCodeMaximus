using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>
/// What <c>session.get</c> and <c>session.headers</c> report in <c>lastUsed</c> for a GPT session (tdcp-spec 10.A9 with FR.33.3 and FR.33.5): the
/// GPT model in its catalog form, an effort only when the model offers it, a sticky GPT effort pick such as <c>xhigh</c>, and the ChatGPT pin.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexLastUsedTests : IAsyncLifetime
{
	private readonly CodexServicesFixture _f = new();

	public async Task InitializeAsync() => await _f.LoadCatalogAsync("codex_1");

	public async Task DisposeAsync() => await _f.DisposeAsync();

	private SessionModelSettings Settings() => new(_f.Settings, _f.Claude, _f.Models);

	private static SessionNodeModel Node(string? model, string? effort) => new() { Name = "n", FileName = "n.txt", ModelId = model, Effort = effort };

	[Fact]
	public void A_GPT_model_is_shown_in_its_catalog_form_with_an_effort_it_offers()
	{
		var shown = Settings().ResolveShown(Node("GPT-X-SOL", "xhigh"));

		Assert.Equal(new SessionModelSettingsModel("gpt-x-sol", "xhigh"), shown);
	}

	[Fact]
	public void FR_33_3_an_effort_the_GPT_model_does_not_offer_is_shown_as_default_and_stays_stored()
	{
		var node  = Node("gpt-x-luna", "high");
		var shown = Settings().ResolveShown(node);

		Assert.Equal(new SessionModelSettingsModel("gpt-x-luna", null), shown);
		Assert.Equal("high", node.Effort);
	}

	[Fact]
	public void Claude_models_keep_the_Claude_efforts()
	{
		var shown = Settings().ResolveShown(Node(FixedClaudeModels.Opus, "max"));

		Assert.Equal(new SessionModelSettingsModel(FixedClaudeModels.Opus, "max"), shown);
	}

	[Fact]
	public void A_sticky_GPT_effort_pick_is_stored()
	{
		var node = Node("gpt-x-sol", null);

		var changed = Settings().RecordPicks(node, new TurnOptionsModel("gpt-x-sol", "xhigh"));

		Assert.True(changed);
		Assert.Equal("xhigh", node.Effort);
		Assert.False(Settings().RecordPicks(node, new TurnOptionsModel("gpt-x-sol", "xhigh"))); // the same pick again changes nothing
	}

	[Fact]
	public void FR_33_5_a_ChatGPT_pin_is_reported_with_its_display_name_and_an_unknown_one_leaves_the_history_value()
	{
		var history = new LastUsedSettingsDto("default", "default", "default", "[Sys] Default");

		var pinned  = SessionRpcModule.ApplyPin(history, new SessionNodeModel { Name = "n", FileName = "n.txt", ProfileId = "codex_1" }, [], _f.Profiles);
		var queued  = SessionRpcModule.ApplyPin(history, new SessionNodeModel { Name = "n", FileName = "n.txt", ProfileId = "p1", PendingProfileId = "codex_1" }, [], _f.Profiles);
		var unknown = SessionRpcModule.ApplyPin(history, new SessionNodeModel { Name = "n", FileName = "n.txt", ProfileId = "codex_9" }, [], _f.Profiles);

		Assert.Equal(("codex_1", "ChatGPT · u@example.com"), (pinned.ProfileId, pinned.ProfileName));
		Assert.Equal("codex_1", queued.ProfileId);
		Assert.Equal(history, unknown);
	}
}
