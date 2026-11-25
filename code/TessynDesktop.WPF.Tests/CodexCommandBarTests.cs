using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.33.1–FR.33.5 in the command bar (TC.CX.04, TC.CX.05, FR.33.17 WPF side): the model picker grouped by engine with the GPT default first and the
/// sign-in hint, the efforts of the selected model, the profiles of its engine, and the toggles a Codex session hides.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexCommandBarTests
{
	private const string Node = "g";

	private readonly FakeTessynClient _client = new() { Catalog = CodexCatalogs.FixtureC() };

	private SessionPaneViewModel CreatePane() =>
		new(_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance);

	private async Task<SessionPaneViewModel> OpenedAsync(LastUsedSettingsDto? lastUsed = null, string engine = RemoteProtocol.Engines.Claude)
	{
		var session = FakeSession.WithEntries(2, "G");
		session.Engine = engine;
		if (lastUsed != null)
			session.LastUsed = lastUsed;
		_client.Sessions[Node] = session;
		var pane = CreatePane();
		await pane.OpenAsync(Node);
		await pane.LoadCatalogAsync();
		return pane;
	}

	private static void Select(SessionPaneViewModel pane, string modelId) => pane.SelectedModel = pane.Models.Single(m => m.Id == modelId);

	// ---- the model picker (FR.33.2, TC.CX.03/TC.CX.04 client side) ----

	[Fact]
	public async Task ModelPicker_GroupsClaudeGptAndLocal_UnderHeaders_WithTheGptDefaultFirst()
	{
		using var pane = await OpenedAsync();

		Assert.Equal(
			["default", "header:Claude", "claude-opus-4-8", "header:GPT (ChatGPT plan)", "gpt-x-sol", "gpt-x-astra", "gpt-x-luna", "header:Local", "qwen3"],
			pane.Models.Select(m => m.Id));
		Assert.Equal(["Claude", "GPT (ChatGPT plan)", "Local"], pane.Models.Where(m => m.IsHeader).Select(m => m.Label));
		Assert.All(pane.Models.Where(m => m.IsHeader), h => Assert.False(h.IsSelectable));
		Assert.All(pane.Models.Where(m => m.Id.StartsWith("gpt-", StringComparison.Ordinal)), m => Assert.True(m.IsCodex));
	}

	[Fact]
	public async Task ModelPicker_ShowsTheDisabledSignInHint_WhenNoChatGptProfileIsSignedIn()
	{
		_client.Catalog = CodexCatalogs.FixtureC(gptHint: "GPT models: sign in to ChatGPT in Settings > Profiles", withGpt: false);

		using var pane = await OpenedAsync();

		var gptHeader = pane.Models.ToList().FindIndex(m => m.Id == "header:GPT (ChatGPT plan)");
		var hint = pane.Models[gptHeader + 1];
		Assert.True(hint.IsHint);
		Assert.False(hint.IsSelectable);
		Assert.Equal("GPT models: sign in to ChatGPT in Settings > Profiles", hint.Label);
		Assert.DoesNotContain(pane.Models, m => m.IsCodex && m.IsSelectable);
	}

	[Fact]
	public async Task ModelPicker_OfAClaudeOnlyDaemon_IsTheFlatListOfBefore()
	{
		_client.Catalog = CodexCatalogs.ClaudeOnly();

		using var pane = await OpenedAsync();

		Assert.Equal(["default", "claude-opus-4-8"], pane.Models.Select(m => m.Id));
		Assert.DoesNotContain(pane.Models, m => !m.IsSelectable);
	}

	[Fact]
	public async Task ModelPicker_ReadsTheListsAgain_WhenTheDaemonSaysTheCatalogChanged()
	{
		// FR.12.13: the Ollama models can arrive after the pane read the catalog (the daemon's lookup finished later, or Settings refreshed the models).
		_client.Catalog = CodexCatalogs.ClaudeOnly();
		using var pane = await OpenedAsync();
		Assert.Equal(["default", "claude-opus-4-8"], pane.Models.Select(m => m.Id));

		_client.Catalog = CodexCatalogs.FixtureC();
		_client.RaiseRaw(RemoteProtocol.ModelsApi.CatalogChangedEvent, "{}");

		Assert.Contains(pane.Models, m => m.Id == "qwen3");
		Assert.Contains(pane.Models, m => m.Id == "gpt-x-sol");
	}

	[Fact]
	public async Task GptModelTooltip_CarriesCodexsUpgradeHint()
	{
		using var pane = await OpenedAsync();

		var astra = pane.Models.Single(m => m.Id == "gpt-x-astra");
		Assert.Contains("GPT-X Astra", astra.ToolTip);
		Assert.Contains("Codex suggests gpt-x-astra-2", astra.ToolTip);
		Assert.Equal("GPT-X Sol", pane.Models.Single(m => m.Id == "gpt-x-sol").ToolTip);
	}

	[Fact]
	public async Task AHeaderIsNeverPickedOrSent()
	{
		using var pane = await OpenedAsync();

		pane.SelectedModel = pane.Models.Single(m => m.Id == "header:Claude");
		pane.InputText = "hi";
		await pane.SendCommand.ExecuteAsync(null);

		Assert.Null(_client.TurnSends.Single().ModelId);
	}

	// ---- efforts per model (FR.33.3, TC.CX.05) ----

	[Fact]
	public async Task Efforts_FollowTheSelectedModel_WithCodexsDescriptionsAsTooltips()
	{
		using var pane = await OpenedAsync();

		Select(pane, "gpt-x-sol");
		Assert.Equal(["default", "low", "medium", "high", "xhigh"], pane.Efforts.Select(e => e.Id));
		Assert.Equal(["Default", "Low", "Medium", "High", "Xhigh"], pane.Efforts.Select(e => e.Label));
		Assert.Equal("Most thorough", pane.Efforts.Single(e => e.Id == "xhigh").ToolTip);

		Select(pane, "gpt-x-luna");
		Assert.Equal(["default", "low", "medium"], pane.Efforts.Select(e => e.Id));

		Select(pane, "claude-opus-4-8");
		Assert.Equal(["Default", "Max", "High", "Medium", "Low"], pane.Efforts.Select(e => e.Label));
	}

	[Fact]
	public async Task AStoredEffortTheModelDoesNotOffer_ShowsAsDefault_SendsNoEffort_AndReturnsWithTheModel()
	{
		using var pane = await OpenedAsync(new LastUsedSettingsDto("gpt-x-sol", "high", CodexCatalogs.ChatGptProfile, CodexCatalogs.ChatGptName), RemoteProtocol.Engines.Codex);
		Assert.Equal("high", pane.SelectedEffort!.Id);

		Select(pane, "gpt-x-luna");
		Assert.Equal("default", pane.SelectedEffort!.Id);
		pane.InputText = "go";
		await pane.SendCommand.ExecuteAsync(null);
		var send = _client.TurnSends.Single();
		Assert.Equal("gpt-x-luna", send.ModelId);
		Assert.Null(send.Effort);

		Select(pane, "gpt-x-sol");
		Assert.Equal("high", pane.SelectedEffort!.Id);
	}

	[Fact]
	public async Task APickedEffort_StaysPicked_ForAModelThatOffersItAgain()
	{
		using var pane = await OpenedAsync();
		Select(pane, "gpt-x-sol");
		pane.SelectedEffort = pane.Efforts.Single(e => e.Id == "xhigh");

		Select(pane, "claude-opus-4-8");
		Assert.Equal("default", pane.SelectedEffort!.Id);
		Select(pane, "gpt-x-sol");

		Assert.Equal("xhigh", pane.SelectedEffort!.Id);
	}

	// ---- profiles per engine (FR.33.5) ----

	[Fact]
	public async Task Profiles_AreTheOnesOfTheSelectedModelsEngine()
	{
		using var pane = await OpenedAsync();
		Assert.Equal(["default", "profile_1"], pane.Profiles.Select(p => p.Id));

		Select(pane, "gpt-x-sol");
		Assert.Equal([CodexCatalogs.ChatGptProfile], pane.Profiles.Select(p => p.Id));
		Assert.Equal(CodexCatalogs.ChatGptName, pane.SelectedProfile!.Label);

		Select(pane, "qwen3");
		Assert.Equal(["default", "profile_1"], pane.Profiles.Select(p => p.Id));
	}

	[Fact]
	public async Task AClaudeProfilePick_IsNotSentWithAGptModel_AndComesBackWithAClaudeModel()
	{
		using var pane = await OpenedAsync();
		pane.SelectedProfile = pane.Profiles.Single(p => p.Id == "profile_1");

		Select(pane, "gpt-x-sol");
		pane.InputText = "go";
		await pane.SendCommand.ExecuteAsync(null);
		var send = _client.TurnSends.Single();
		Assert.Equal("gpt-x-sol", send.ModelId);
		Assert.NotEqual("profile_1", send.ProfileId);

		Select(pane, "claude-opus-4-8");
		Assert.Equal("profile_1", pane.SelectedProfile!.Id);
	}

	[Fact]
	public async Task AGptSend_CarriesTheModelTheEffortAndTheChatGptProfile()
	{
		var catalog = CodexCatalogs.FixtureC();
		_client.Catalog = catalog with { Profiles = [.. catalog.Profiles, new ProfileOptionDto("codex_2", "ChatGPT · w@example.com", RemoteProtocol.Engines.Codex, "pro")] };
		using var pane = await OpenedAsync();
		Select(pane, "gpt-x-sol");
		Assert.Equal(CodexCatalogs.ChatGptProfile, pane.SelectedProfile!.Id);   // shown, but not picked: the daemon applies its inherit rule (FR.33.5)
		pane.SelectedEffort = pane.Efforts.Single(e => e.Id == "medium");
		pane.SelectedProfile = pane.Profiles.Single(p => p.Id == "codex_2");
		pane.InputText = "go";

		await pane.SendCommand.ExecuteAsync(null);

		var send = _client.TurnSends.Single();
		Assert.Equal(("gpt-x-sol", "medium", "codex_2"), (send.ModelId, send.Effort, send.ProfileId));
	}

	// ---- a Codex session (FR.33.1, FR.33.8) ----

	[Fact]
	public async Task AGptModel_HidesTheStreamingAndToolsToggles_AndAClaudeModelShowsStreamingAgain()
	{
		using var pane = await OpenedAsync();
		Assert.False(pane.IsCodexSession);
		Assert.True(pane.Toggles.IsStreamingApplicable);

		Select(pane, "gpt-x-sol");
		Assert.True(pane.IsCodexSession);
		Assert.False(pane.Toggles.IsStreamingApplicable);
		Assert.False(pane.LocalTools.IsVisible);

		Select(pane, "claude-opus-4-8");
		Assert.False(pane.IsCodexSession);
		Assert.True(pane.Toggles.IsStreamingApplicable);
	}

	[Fact]
	public async Task AStoredCodexSession_IsACodexSession_BeforeTheCatalogIsRead()
	{
		var session = FakeSession.WithEntries(2, "G");
		session.Engine = RemoteProtocol.Engines.Codex;
		_client.Sessions[Node] = session;
		using var pane = CreatePane();

		await pane.OpenAsync(Node);

		Assert.True(pane.IsCodexSession);
		Assert.False(pane.Toggles.IsStreamingApplicable);
	}

	[Fact]
	public async Task ACodexSessionsSelectors_CanChangeWhileATurnRuns_AndTheNextSendUsesThem()
	{
		using var pane = await OpenedAsync(new LastUsedSettingsDto("gpt-x-sol", "low", CodexCatalogs.ChatGptProfile, CodexCatalogs.ChatGptName), RemoteProtocol.Engines.Codex);
		pane.InputText = "first";
		await pane.SendCommand.ExecuteAsync(null);

		Select(pane, "gpt-x-luna");
		pane.SelectedEffort = pane.Efforts.Single(e => e.Id == "medium");
		pane.InputText = "second";
		await pane.SendCommand.ExecuteAsync(null);

		var second = _client.TurnSends.Last();
		Assert.Equal(("gpt-x-luna", "medium"), (second.ModelId, second.Effort));
	}
}
