using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels.Settings;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.33.17 in Settings > General (TC.CX.36 client side, TC.CX.41 VM): the Codex path with Browse, the status line and version note, the catalog profile
/// from the signed-in ChatGPT profiles, the editable GPT tier table with Reset to defaults, the minimal patch, validation, and the close guard (FR.4.7).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexGeneralSettingsTests
{
	private const string Exe = @"C:\Tools\Codex\codex.exe";

	private static readonly CodexModelTierDto[] Defaults = [new("astra", 3), new("sol", 2), new("terra", 2), new("luna", 1), new("gpt", 2)];

	private readonly FakeTessynClient _client = new FakeTessynClient().AnnouncingSettings().WithSettingsValues();
	private readonly ScriptedPicker _picker = new();

	private sealed class ScriptedPicker : IFilePicker
	{
		public string? Answer { get; set; }

		public List<string> Titles { get; } = [];

		public Task<string?> PickAsync(string title, string filter, string? initialPath = null)
		{
			Titles.Add(title);
			return Task.FromResult(Answer);
		}
	}

	public CodexGeneralSettingsTests()
	{
		_client.ServerInfo = _client.ServerInfo! with { Capabilities = [.. _client.ServerInfo.Capabilities, RemoteProtocol.CodexProfilesAdmin.Capability] };
		var general = (GeneralSettingsDto)_client.SettingsValues[RemoteProtocol.SettingsApi.GeneralSection];
		_client.SettingsValues[RemoteProtocol.SettingsApi.GeneralSection] = general with
		{
			CodexPath = string.Empty,
			CodexStatus = new CodexStatusDto(true, Exe, "0.172.0", "0.171.x", "Codex 0.172.0 is newer than the tested 0.171.x. If GPT sessions misbehave, install 0.171.x.",
				$"Codex 0.172.0 found at {Exe}"),
			CodexCatalogProfileId = null,
			CodexModelTiers = Defaults,
			CodexModelTiersAreDefaults = true,
		};
		_client.CodexProfiles =
		[
			new("codex-default", "ChatGPT · me@example.com", "me@example.com", "plus", true, false, false, false),
			new("codex_1", "ChatGPT · u@example.com", "u@example.com", "plus", false, true, false, false),
		];
		var baseApplier = _client.SettingsPatchApplier!;
		_client.SettingsPatchApplier = (section, patch) =>
		{
			var applied = baseApplier(section, patch);
			if (patch is not GeneralSettingsPatch g || applied is not GeneralSettingsDto values)
				return applied;

			return values with
			{
				CodexPath = g.CodexPath ?? values.CodexPath,
				CodexCatalogProfileId = g.CodexCatalogProfileId == null ? values.CodexCatalogProfileId : g.CodexCatalogProfileId.Length == 0 ? null : g.CodexCatalogProfileId,
				CodexModelTiers = g.ResetCodexModelTiers == true ? Defaults : g.CodexModelTiers ?? values.CodexModelTiers,
				CodexModelTiersAreDefaults = g.ResetCodexModelTiers == true || (g.CodexModelTiers == null && values.CodexModelTiersAreDefaults),
			};
		};
	}

	private async Task<GeneralSectionViewModel> LoadedAsync()
	{
		var general = new GeneralSectionViewModel(_client, NullLogger<GeneralSectionViewModel>.Instance, _picker);
		await general.InitializeAsync(RemoteProtocol.SettingsApi.Capability);
		return general;
	}

	private GeneralSettingsPatch SentPatch() => Assert.IsType<GeneralSettingsPatch>(Assert.Single(_client.SettingsUpdates).Patch);

	[Fact]
	public async Task TheCodexSection_ShowsTheStatusLineTheVersionNoteAndTheTestedVersion()
	{
		var general = await LoadedAsync();

		Assert.True(general.HasCodexEngine);
		Assert.True(general.IsCodexFound);
		Assert.Equal($"Codex 0.172.0 found at {Exe}", general.CodexStatusLine);
		Assert.True(general.HasCodexNote);
		Assert.Contains("newer than the tested 0.171.x", general.CodexNote);
		Assert.Equal("Tested with Codex 0.171.x.", general.CodexTestedText);
		Assert.Equal(string.Empty, general.CodexPath);
		Assert.False(general.HasChanges);
	}

	[Fact]
	public async Task CodexNotFound_ShowsTheWingetHint()
	{
		var values = (GeneralSettingsDto)_client.SettingsValues[RemoteProtocol.SettingsApi.GeneralSection];
		_client.SettingsValues[RemoteProtocol.SettingsApi.GeneralSection] = values with
		{
			CodexStatus = new CodexStatusDto(false, null, null, "0.171.x", null, "Codex not found. Install it with: winget install OpenAI.Codex"),
		};

		var general = await LoadedAsync();

		Assert.False(general.IsCodexFound);
		Assert.Equal("Codex not found. Install it with: winget install OpenAI.Codex", general.CodexStatusLine);
		Assert.False(general.HasCodexNote);
	}

	[Fact]
	public async Task ADaemonWithoutCodex_HidesTheSection_AndSendsNoCodexMember()
	{
		var values = (GeneralSettingsDto)_client.SettingsValues[RemoteProtocol.SettingsApi.GeneralSection];
		_client.SettingsValues[RemoteProtocol.SettingsApi.GeneralSection] = values with { CodexStatus = null, CodexModelTiers = null };
		var general = await LoadedAsync();

		Assert.False(general.HasCodexEngine);
		general.HttpsProxy = "http://p:1";
		Assert.True(await general.SaveAsync());

		var sent = SentPatch();
		Assert.Null(sent.CodexPath);
		Assert.Null(sent.CodexModelTiers);
		Assert.Null(sent.ResetCodexModelTiers);
	}

	[Fact]
	public async Task Browse_PutsThePickedExeInThePath_AndSaveSendsOnlyIt()
	{
		var general = await LoadedAsync();
		_picker.Answer = @"D:\Codex\codex.exe";

		await general.BrowseCodexPathCommand.ExecuteAsync(null);

		Assert.Equal(@"D:\Codex\codex.exe", general.CodexPath);
		Assert.True(general.HasChanges);
		Assert.True(await general.SaveAsync());
		var sent = SentPatch();
		Assert.Equal(@"D:\Codex\codex.exe", sent.CodexPath);
		Assert.Null(sent.CodexModelTiers);
		Assert.Null(sent.CodexCatalogProfileId);
		Assert.Null(sent.HttpsProxy);
	}

	[Fact]
	public async Task TheCatalogProfileList_IsAutomaticPlusTheSignedInChatGptProfiles()
	{
		var general = await LoadedAsync();

		Assert.Equal([string.Empty, "codex_1"], general.CatalogProfiles.Select(o => o.Value));
		Assert.Equal(string.Empty, general.CodexCatalogProfileId);

		general.CodexCatalogProfileId = "codex_1";
		Assert.True(await general.SaveAsync());
		Assert.Equal("codex_1", SentPatch().CodexCatalogProfileId);
		Assert.Equal("codex_1", general.CodexCatalogProfileId);
	}

	[Fact]
	public async Task AStoredCatalogProfileThatIsNoLongerSignedIn_StaysSelected()
	{
		var values = (GeneralSettingsDto)_client.SettingsValues[RemoteProtocol.SettingsApi.GeneralSection];
		_client.SettingsValues[RemoteProtocol.SettingsApi.GeneralSection] = values with { CodexCatalogProfileId = "codex_9" };

		var general = await LoadedAsync();

		Assert.Equal("codex_9", general.CodexCatalogProfileId);
		Assert.Contains(general.CatalogProfiles, o => o.Value == "codex_9");
		Assert.False(general.HasChanges);
	}

	[Fact]
	public async Task TheTierTable_CanBeEdited_Reordered_AndSavedWhole()
	{
		var general = await LoadedAsync();
		Assert.Equal(["astra", "sol", "terra", "luna", "gpt"], general.Tiers.Select(t => t.Match));
		Assert.Equal(Constants.Codex.TiersAreDefaults, general.TierTableNote);

		general.Tiers.Single(t => t.Match == "luna").Tier = 0;
		general.AddTierCommand.Execute(null);
		general.Tiers.Last().Match = "nova";
		general.MoveTierUpCommand.Execute(general.Tiers.Last());
		general.RemoveTierCommand.Execute(general.Tiers.Single(t => t.Match == "terra"));

		Assert.True(general.HasChanges);
		Assert.True(await general.SaveAsync());
		Assert.Equal(
			new CodexModelTierDto[] { new("astra", 3), new("sol", 2), new("luna", 0), new("nova", 2), new("gpt", 2) },
			SentPatch().CodexModelTiers);
		Assert.Null(SentPatch().ResetCodexModelTiers);
	}

	[Theory]
	[InlineData("", "needs a text")]
	[InlineData("SOL", "listed twice")]
	public async Task AnInvalidTierRow_IsNotSent_AndTheErrorSaysWhy(string match, string error)
	{
		var general = await LoadedAsync();
		general.Tiers[0].Match = match;

		Assert.True(general.HasChanges);
		Assert.False(await general.SaveAsync());

		Assert.Empty(_client.SettingsUpdates);
		Assert.Contains(error, general.ErrorText);
	}

	[Fact]
	public async Task ResetToDefaults_IsAnEdit_SentAsResetCodexModelTiers_OnSave()
	{
		var values = (GeneralSettingsDto)_client.SettingsValues[RemoteProtocol.SettingsApi.GeneralSection];
		_client.SettingsValues[RemoteProtocol.SettingsApi.GeneralSection] = values with
		{
			CodexModelTiers = [new("astra", 4), new("gpt", 1)], CodexModelTiersAreDefaults = false,
		};
		var general = await LoadedAsync();
		Assert.Equal(string.Empty, general.TierTableNote);

		general.ResetTiersCommand.Execute(null);

		Assert.True(general.IsTierResetPending);
		Assert.False(general.AreTiersEditable);
		Assert.Equal(Constants.Codex.TiersResetPending, general.TierTableNote);
		Assert.False(general.ResetTiersCommand.CanExecute(null));
		Assert.True(general.HasChanges);

		Assert.True(await general.SaveAsync());
		var sent = SentPatch();
		Assert.True(sent.ResetCodexModelTiers);
		Assert.Null(sent.CodexModelTiers);
		Assert.False(general.IsTierResetPending);
		Assert.Equal(["astra", "sol", "terra", "luna", "gpt"], general.Tiers.Select(t => t.Match));
		Assert.True(general.TiersAreDefaults);
	}

	[Fact]
	public async Task AnUnsavedCodexEdit_IsCaughtByTheCloseGuard()
	{
		var presenter = new FakeDialogPresenter { Answer = d => { d.CancelCommand.Execute(null); return Task.CompletedTask; } };
		var settings = SettingsTestSupport.NewSettings(_client, presenter: presenter);
		await settings.InitializeAsync();

		settings.General.ResetTiersCommand.Execute(null);

		Assert.Contains("General", settings.UnsavedTabs);
		Assert.False(await settings.ConfirmCloseAsync());
	}
}
