using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// The daemon-owned settings sections (package J3): values, versions, patch updates, the whitelist that keeps a patch inside its section, validation,
/// and per-section change detection that also sees a save made by the Avalonia Settings window.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SettingsSectionServiceTests : IDisposable
{
	/// <summary>Every top-level member of <see cref="AppSettingsModel"/> a section may write; the rest of the model must never change.</summary>
	private static readonly string[] WhitelistedModelMembers =
	[
		nameof(AppSettingsModel.HttpsProxy), nameof(AppSettingsModel.OllamaBaseUrl), nameof(AppSettingsModel.AgentToolsEnabled), nameof(AppSettingsModel.FileAccess), nameof(AppSettingsModel.MediaPreviewHosts),
		nameof(AppSettingsModel.NotificationsEnabled), nameof(AppSettingsModel.SessionCompletionSoundFile), nameof(AppSettingsModel.SessionCompletionSoundThrottleSeconds),
		nameof(AppSettingsModel.TelegramEnabled), nameof(AppSettingsModel.TelegramChatId), nameof(AppSettingsModel.TelegramAllowedUserIds),
		nameof(AppSettingsModel.TelegramMentionHandle), nameof(AppSettingsModel.TelegramMirrorMode),
	];

	private readonly string _dir;
	private readonly AppSettingsService _settings;
	private readonly RemoteEventBus _bus = new();
	private readonly SettingsSectionService _service;
	private readonly List<SettingsChangedPayload> _changes = [];

	public SettingsSectionServiceTests()
	{
		_dir = Path.Combine(Path.GetTempPath(), "settings-sections-" + Guid.NewGuid().ToString("N")[..8]);
		Directory.CreateDirectory(_dir);
		_settings = new AppSettingsService(_dir);
		_settings.Settings.SessionFilesRoot = Path.Combine(_dir, "sessions");
		_service = NewService(_settings);
		_bus.Published += n =>
		{
			if (n.Method == RemoteProtocol.SettingsApi.ChangedEvent)
				lock (_changes) _changes.Add(n.Params.Deserialize<SettingsChangedPayload>(TdcpJsonOptions.Default)!);
		};
	}

	public void Dispose()
	{
		_service.Dispose();
		_bus.Dispose();
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, recursive: true);
	}

	private SettingsSectionService NewService(IAppSettingsService settings) =>
		new(settings, [new GeneralSettingsSection(), new NotificationsSettingsSection(), new TelegramSettingsSection()], _bus);

	private static JsonNode Patch(string json) => JsonNode.Parse(json)!;

	private long Version(string section) => _service.GetSections().Single(s => s.Name == section).Version;

	// ── values and versions ──────────────────────────────────────────────────────────────────

	[Fact]
	public void Sections_AreTheThreeDaemonOwnedOnes_AllAtTheInitialVersion()
	{
		var sections = _service.GetSections();

		Assert.Equal(["general", "notifications", "telegram"], sections.Select(s => s.Name));
		Assert.All(sections, s => Assert.Equal(1, s.Version));
	}

	[Fact]
	public void Get_ReadsTheValuesOfTheModel()
	{
		_settings.Settings.ClaudePath = "claude-x";
		_settings.Settings.MediaPreviewHosts = ["a.example.com"];
		_settings.Settings.TelegramChatId = -1001;
		_settings.Settings.TelegramAllowedUserIds = [7, 9];
		_settings.Settings.TelegramMirrorMode = TelegramMirrorMode.Off;
		using var service = NewService(_settings); // a daemon that started with these values

		var general = service.Get("general");
		var telegram = service.Get("telegram");

		var g = Assert.IsType<GeneralSettingsDto>(general.Values);
		Assert.Equal(("general", 1L, "claude-x"), (general.Section, general.Version, g.ClaudePath));
		Assert.Equal(["a.example.com"], g.MediaPreviewHosts);
		Assert.Equal(FileAccessModeDto.WorkingDirectory, g.FileAccess.Mode);
		var t = Assert.IsType<TelegramSettingsDto>(telegram.Values);
		Assert.Equal((-1001L, TelegramMirrorModeDto.Off), (t.TelegramChatId, t.TelegramMirrorMode));
		Assert.Equal([7L, 9L], t.TelegramAllowedUserIds);
	}

	[Fact]
	public void Update_ChangesOnlyThePatchedFields_SavesThem_BumpsOnlyThatSection_AndAnnouncesIt()
	{
		var before = _service.Get("notifications").Values;

		var version = _service.Update("notifications", Patch("""{"sessionCompletionSoundThrottleSeconds":30}"""), baseVersion: 1);

		Assert.Equal(2, version);
		var after = Assert.IsType<NotificationsSettingsDto>(_service.Get("notifications").Values);
		Assert.Equal(((NotificationsSettingsDto)before) with { SessionCompletionSoundThrottleSeconds = 30 }, after);
		Assert.Equal(30, ReloadedSettings().SessionCompletionSoundThrottleSeconds);
		Assert.Equal([1L, 2L, 1L], _service.GetSections().Select(s => s.Version));
		Assert.Equal([new SettingsChangedPayload("notifications", 2)], _changes);
	}

	[Fact]
	public void Update_WithAStaleBaseVersion_IsRefusedAndChangesNothing()
	{
		_service.Update("notifications", Patch("""{"notificationsEnabled":false}"""), 1);

		var ex = Assert.Throws<TdcpException>(() => _service.Update("notifications", Patch("""{"notificationsEnabled":true}"""), 1));

		Assert.Equal("Settings changed", ex.Message);
		Assert.False(_settings.Settings.NotificationsEnabled);
		Assert.Equal(2, Version("notifications"));
	}

	[Fact]
	public void Update_ThatChangesNothing_SavesNothing_AndKeepsTheVersion()
	{
		var counting = new SaveCountingAppSettings(_settings);
		using var service = NewService(counting);

		var version = service.Update("general", Patch("""{"ollamaBaseUrl":"http://localhost:11434"}"""), 1); // the default already

		Assert.Equal(1, version);
		Assert.Equal(0, counting.SaveCount);
		Assert.Empty(_changes);
	}

	[Fact]
	public void Update_WorksWithAServiceWhoseSavedEventDoesNothing()
	{
		var counting = new SaveCountingAppSettings(_settings); // forwards Save, but not the Saved event
		using var service = NewService(counting);

		var version = service.Update("telegram", Patch("""{"telegramEnabled":true}"""), 1);

		Assert.Equal(2, version);
		Assert.Equal(1, counting.SaveCount);
		Assert.Contains(new SettingsChangedPayload("telegram", 2), _changes);
	}

	[Fact]
	public async Task Update_TwoWritersFromTheSameVersion_OneWins_TheOtherIsStale()
	{
		var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() =>
		{
			try
			{
				return _service.Update("telegram", Patch($$"""{"telegramChatId":{{i + 1}}}"""), 1);
			}
			catch (TdcpException)
			{
				return -1L;
			}
		})));

		Assert.Single(results, v => v == 2);
		Assert.Equal(7, results.Count(v => v == -1));
	}

	// ── Avalonia Settings save, per-section change detection ─────────────────────────────────

	[Fact]
	public void ASaveWithChangesInTwoSections_BumpsAndAnnouncesExactlyThose()
	{
		_settings.Settings.TelegramMentionHandle = "@me";      // what the Avalonia Settings window does: set the members, then Save
		_settings.Settings.NotificationsEnabled = false;
		_settings.Save();

		Assert.Equal(["notifications", "telegram"], _changes.Select(c => c.Section).Order());
		Assert.Equal((1L, 2L, 2L), (Version("general"), Version("notifications"), Version("telegram")));
	}

	[Fact]
	public void ASaveThatChangesNoSection_IsSilent()
	{
		_settings.Settings.Theme = "Light";                      // not daemon-owned
		_settings.Settings.Tree.Add(new DirectoryNodeModel { Path = @"C:\p" });
		_settings.Save();
		_settings.Save();

		Assert.Empty(_changes);
		Assert.Equal([1L, 1L, 1L], _service.GetSections().Select(s => s.Version));
	}

	[Fact]
	public void AnExternalChangeStalesAnOpenEditor()
	{
		var seen = _service.Get("general").Version;
		_settings.Settings.HttpsProxy = "http://127.0.0.1:8080";
		_settings.Save();

		Assert.Throws<TdcpException>(() => _service.Update("general", Patch("""{"ollamaBaseUrl":"http://localhost:1234"}"""), seen));
	}

	// ── The whitelist ────────────────────────────────────────────────────────────────────────

	[Theory]
	[InlineData("general", """{"claudePath":"C:\\tmp\\evil.exe"}""")]               // decision U1: read-only on the wire
	[InlineData("general", """{"sourceCodesLocation":"C:\\src"}""")]
	[InlineData("general", """{"sessionFilesRoot":"C:\\files"}""")]
	[InlineData("general", """{"ClaudePath":"x"}""")]                                    // a case variant is the same (absent) member
	[InlineData("general", """{"$type":"System.Object"}""")]
	[InlineData("general", """{"fileAccess":{"Mode":"referencedOnly","ModeRevision":1}}""")]
	[InlineData("general", """{"tdcpToken":"x"}""")]
	[InlineData("general", """{"tree":[]}""")]
	[InlineData("general", """{"profiles":[]}""")]
	[InlineData("general", """{"externalMcpServers":[]}""")]
	[InlineData("general", """{"schedules":[]}""")]
	[InlineData("general", """{"agentMcpPort":1}""")]
	[InlineData("general", """{"diagnostics":{}}""")]
	[InlineData("general", """{"theme":"Light"}""")]
	[InlineData("general", """{"telegramEnabled":true}""")]
	[InlineData("general", """{"fileAccess":{"modeRevision":9}}""")]
	[InlineData("notifications", """{"claudePath":"x"}""")]
	[InlineData("notifications", """{"telegramMentionHandle":"@x"}""")]
	[InlineData("telegram", """{"telegramBotToken":"123456789:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""")]
	[InlineData("telegram", """{"telegramTopicThreadIdsByNodeId":{"a":1}}""")]
	[InlineData("telegram", """{"telegramTopicNamesByNodeId":{"a":"b"}}""")]
	[InlineData("telegram", """{"telegramUpdateOffset":5}""")]
	[InlineData("telegram", """{"sessionFilesRoot":"C:\\x"}""")]
	public void Update_RefusesAMemberOutsideTheSectionsWhitelist_AndChangesNothing(string section, string patch)
	{
		var snapshot = ModelJson();

		Assert.Throws<TdcpInvalidParamsException>(() => _service.Update(section, Patch(patch), 1));

		Assert.Equal(snapshot, ModelJson());
		Assert.Equal(1, Version(section));
	}

	[Fact]
	public void Update_ARejectedMemberNextToAValidOne_AppliesNeitherOfThem()
	{
		Assert.Throws<TdcpInvalidParamsException>(() => _service.Update("telegram", Patch("""{"telegramEnabled":true,"tdcpToken":"x"}"""), 1));

		Assert.False(_settings.Settings.TelegramEnabled);
	}

	[Fact]
	public void Update_OfEveryWhitelistedField_ChangesNothingElseInTheModel()
	{
		SeedEverythingElse();
		var before = ModelObject();

		_service.Update("general", Patch($$$"""
			{"httpsProxy":"http://p:1",
			 "ollamaBaseUrl":"http://o:2","agentToolsEnabled":false,"mediaPreviewHosts":["*.x.com","y.org"],
			 "fileAccess":{"mode":"referencedOnly","maxRelayChunkBytes":1000,"maxTextFileBytes":2000,"maxImageFileBytes":3000,"maxPdfFileBytes":4000,"maxVideoFileBytes":5000}}
			"""), 1);
		_service.Update("notifications", Patch($$"""{"notificationsEnabled":false,"sessionCompletionSoundFile":{{Json(Path.Combine(_dir, "s.wav"))}},"sessionCompletionSoundThrottleSeconds":5}"""), 1);
		_service.Update("telegram", Patch("""{"telegramEnabled":true,"telegramChatId":-100,"telegramAllowedUserIds":[1,2],"telegramMentionHandle":"@boss","telegramMirrorMode":"full"}"""), 1);

		var after = ModelObject();
		foreach (var member in WhitelistedModelMembers)
		{
			Assert.NotEqual(before[member]?.ToJsonString(), after[member]?.ToJsonString()); // every whitelisted member really was written...
			before.Remove(member);
			after.Remove(member);
		}

		Assert.Equal(before.ToJsonString(), after.ToJsonString()); // ...and nothing else moved
		Assert.Equal(2, _settings.Settings.FileAccess.ModeRevision);
		Assert.Equal(("claude", string.Empty), (_settings.Settings.ClaudePath, _settings.Settings.SourceCodesLocation)); // read-only: untouched
		Assert.Equal(["a"], _settings.Settings.TelegramTopicThreadIdsByNodeId.Keys.ToArray());
	}

	[Fact]
	public void TheSectionDtos_HaveNoSecretMembers_AndEachPatchMirrorsItsSection()
	{
		(Type Dto, Type Patch)[] pairs =
		[
			(typeof(GeneralSettingsDto), typeof(GeneralSettingsPatch)),
			(typeof(NotificationsSettingsDto), typeof(NotificationsSettingsPatch)),
			(typeof(TelegramSettingsDto), typeof(TelegramSettingsPatch)),
			(typeof(FileAccessSettingsDto), typeof(FileAccessSettingsPatch)),
		];

		foreach (var (dto, patch) in pairs)
		{
			var dtoNames = dto.GetProperties().Select(p => p.Name).Order().ToArray();
			string[] readOnly = dto == typeof(GeneralSettingsDto) ? ["ClaudePath", "SessionFilesRoot", "SourceCodesLocation"] : [];
			Assert.Equal(dtoNames.Except(readOnly), patch.GetProperties().Select(p => p.Name).Order());
			Assert.DoesNotContain(dtoNames, n => n.Contains("token", StringComparison.OrdinalIgnoreCase) || n.Contains("secret", StringComparison.OrdinalIgnoreCase)
				|| n.Contains("password", StringComparison.OrdinalIgnoreCase) || n.Contains("topic", StringComparison.OrdinalIgnoreCase));
		}

		Assert.Equal(
			["AgentToolsEnabled", "ClaudePath", "FileAccess", "HttpsProxy", "MediaPreviewHosts", "OllamaBaseUrl", "SessionFilesRoot", "SourceCodesLocation"],
			typeof(GeneralSettingsDto).GetProperties().Select(p => p.Name).Order());
		Assert.Equal(
			["NotificationsEnabled", "SessionCompletionSoundFile", "SessionCompletionSoundThrottleSeconds"],
			typeof(NotificationsSettingsDto).GetProperties().Select(p => p.Name).Order());
		Assert.Equal(
			["TelegramAllowedUserIds", "TelegramChatId", "TelegramEnabled", "TelegramMentionHandle", "TelegramMirrorMode"],
			typeof(TelegramSettingsDto).GetProperties().Select(p => p.Name).Order());
	}

	// ── Validation ───────────────────────────────────────────────────────────────────────────

	[Theory]
	[InlineData("general", """{"httpsProxy":"not a url"}""")]
	[InlineData("general", """{"httpsProxy":"ftp://p:1"}""")]
	[InlineData("general", """{"ollamaBaseUrl":""}""")]
	[InlineData("general", """{"ollamaBaseUrl":"localhost:11434"}""")]
	[InlineData("general", """{"mediaPreviewHosts":["http://x.com/y"]}""")]
	[InlineData("general", """{"mediaPreviewHosts":["a b"]}""")]
	[InlineData("general", """{"mediaPreviewHosts":["*.com"]}""")]        // decision U5: a wildcard needs two labels
	[InlineData("general", """{"mediaPreviewHosts":["*.io","ok.example.com"]}""")]
	[InlineData("general", """{"mediaPreviewHosts":[""]}""")]
	[InlineData("general", """{"fileAccess":{"mode":"everything"}}""")]
	[InlineData("general", """{"fileAccess":{"mode":7}}""")]
	[InlineData("general", """{"fileAccess":{"maxTextFileBytes":0}}""")]
	[InlineData("general", """{"fileAccess":{"maxRelayChunkBytes":999999999}}""")]
	[InlineData("general", """{"agentToolsEnabled":"yes"}""")]
	[InlineData("notifications", """{"sessionCompletionSoundThrottleSeconds":-1}""")]
	[InlineData("notifications", """{"sessionCompletionSoundThrottleSeconds":100000000}""")]
	[InlineData("notifications", """{"sessionCompletionSoundFile":"C:\\sounds\\a.mp3"}""")]
	[InlineData("notifications", """{"sessionCompletionSoundFile":"a.wav"}""")]
	[InlineData("notifications", """{"sessionCompletionSoundFile":"\\\\h\\s\\a.wav"}""")]   // UNC (decision U2)
	[InlineData("notifications", """{"sessionCompletionSoundFile":"//h/s/a.wav"}""")]
	[InlineData("notifications", """{"sessionCompletionSoundFile":"\\foo.wav"}""")]         // root-relative
	[InlineData("notifications", """{"sessionCompletionSoundFile":"C:foo.wav"}""")]          // drive-relative
	[InlineData("notifications", """{"sessionCompletionSoundFile":"\\\\?\\C:\\x.wav"}""")]   // device path
	[InlineData("telegram", """{"telegramAllowedUserIds":[0]}""")]
	[InlineData("telegram", """{"telegramAllowedUserIds":[-5]}""")]
	[InlineData("telegram", """{"telegramMentionHandle":"boss"}""")]
	[InlineData("telegram", """{"telegramMentionHandle":"@bo ss"}""")]
	[InlineData("telegram", """{"telegramMirrorMode":"sometimes"}""")]
	[InlineData("telegram", """{"telegramMirrorMode":99}""")]
	[InlineData("telegram", """{"telegramChatId":"abc"}""")]
	public void Update_RejectsAnInvalidValue_AndChangesNothing(string section, string patch)
	{
		var snapshot = ModelJson();

		Assert.Throws<TdcpInvalidParamsException>(() => _service.Update(section, Patch(patch), 1));

		Assert.Equal(snapshot, ModelJson());
	}

	[Fact]
	public void Update_RejectsAPatchThatIsNoObject_AndAnUnknownSection()
	{
		Assert.Throws<TdcpInvalidParamsException>(() => _service.Update("general", Patch("[1]"), 1));
		Assert.Throws<TdcpInvalidParamsException>(() => _service.Update("general", Patch("\"x\""), 1));
		Assert.Throws<TdcpInvalidParamsException>(() => _service.Update("nope", Patch("{}"), 1));
		Assert.Throws<TdcpInvalidParamsException>(() => _service.Get("mcp"));
	}

	[Fact]
	public void Update_AcceptsAListAtTheLimit()
	{
		var hosts = string.Join(",", Enumerable.Range(0, Constants.SettingsApi.MaxListEntries).Select(i => $"\"h{i}.example.com\""));

		_service.Update("general", Patch($$"""{"mediaPreviewHosts":[{{hosts}}]}"""), 1);

		Assert.Equal(Constants.SettingsApi.MaxListEntries, _settings.Settings.MediaPreviewHosts.Count);
	}

	[Fact]
	public void Update_ACaseVariantAndADuplicateKey_StayInsideTheWhitelist_TheLastOneWins()
	{
		_service.Update("general", Patch("""{"OllamaBaseUrl":"http://a:1","ollamabaseurl":"http://b:2"}"""), 1);

		Assert.Equal("http://b:2", _settings.Settings.OllamaBaseUrl);
	}

	[Fact]
	public void Update_WhenTheSaveFails_TheModelAndTheVersionAreAsBefore()
	{
		var failing = new ThrowingSaveAppSettings(_settings);
		using var service = NewService(failing);
		failing.FailSaves = true;
		var before = ModelJson();

		Assert.Throws<IOException>(() => service.Update("general", Patch("""{"httpsProxy":"http://p:1","fileAccess":{"mode":"referencedOnly"},"mediaPreviewHosts":["x.example.com"]}"""), 1));
		Assert.Throws<IOException>(() => service.Update("telegram", Patch("""{"telegramChatId":5,"telegramAllowedUserIds":[1]}"""), 1));
		Assert.Throws<IOException>(() => service.Update("notifications", Patch("""{"notificationsEnabled":false}"""), 1));

		Assert.Equal(before, ModelJson());
		Assert.Equal([1L, 1L, 1L], service.GetSections().Select(s => s.Version));
		failing.FailSaves = false;
		Assert.Equal(2, service.Update("telegram", Patch("""{"telegramChatId":5}"""), 1)); // the version was not consumed
	}

	[Fact]
	public void Update_RefusesAListOverTheLimit()
	{
		var hosts = string.Join(",", Enumerable.Range(0, Constants.SettingsApi.MaxListEntries + 1).Select(i => $"\"h{i}.example.com\""));

		Assert.Throws<TdcpInvalidParamsException>(() => _service.Update("general", Patch($$"""{"mediaPreviewHosts":[{{hosts}}]}"""), 1));
	}

	[Fact]
	public void Update_NormalisesTheLists_HostsLowercasedAndTrimmedAndUnique_UserIdsUnique()
	{
		_service.Update("general", Patch("""{"mediaPreviewHosts":[" Img.Example.com ","img.example.com","*.CDN.org"]}"""), 1);
		_service.Update("telegram", Patch("""{"telegramAllowedUserIds":[3,3,4]}"""), 1);

		Assert.Equal(["img.example.com", "*.cdn.org"], _settings.Settings.MediaPreviewHosts);
		Assert.Equal([3L, 4L], _settings.Settings.TelegramAllowedUserIds);
	}

	[Fact]
	public void Update_AnEmptyListAndEmptyOptionalTexts_AreAccepted_AndClearTheValues()
	{
		_settings.Settings.HttpsProxy = "http://p:1";
		_settings.Settings.TelegramMentionHandle = "@me";
		using var service = NewService(_settings);

		service.Update("general", Patch("""{"mediaPreviewHosts":[],"httpsProxy":""}"""), 1);
		service.Update("telegram", Patch("""{"telegramMentionHandle":"","telegramAllowedUserIds":[]}"""), 1);

		Assert.Empty(_settings.Settings.MediaPreviewHosts);
		Assert.Equal(string.Empty, _settings.Settings.HttpsProxy);
		Assert.Equal(string.Empty, _settings.Settings.TelegramMentionHandle);
	}

	// ── helpers ──────────────────────────────────────────────────────────────────────────────

	private AppSettingsModel ReloadedSettings()
	{
		var other = new AppSettingsService(_dir);
		other.Load();
		return other.Settings;
	}

	private string ModelJson() => ModelObject().ToJsonString();

	private JsonObject ModelObject() => (JsonObject)JsonSerializer.SerializeToNode(_settings.Settings)!;

	private static string Json(string text) => JsonSerializer.Serialize(text);

	/// <summary>Gives the members outside every section recognisable values, so that a patch touching any of them would show.</summary>
	private void SeedEverythingElse()
	{
		var model = _settings.Settings;
		model.Theme = "Light";
		model.TdcpToken = "legacy-token-value";
		model.AgentMcpPort = 4321;
		model.Tree.Add(new DirectoryNodeModel { Path = @"C:\seeded" });
		model.Profiles.Add(new ClaudeProfileModel { ProfileId = "p1", DisplayName = "x" });
		model.SelectedProfileIndex = 1;
		model.TelegramTopicThreadIdsByNodeId["a"] = 1;
		model.TelegramTopicNamesByNodeId["a"] = "topic";
		model.TelegramUpdateOffset = 77;
		model.ScreensaverDirectory = @"C:\pics";
		model.ExternalMcpServers.Add(new ExternalMcpServerModel { Id = "m1", StaticHeaderValue = "hunter2" });
		model.FileAccess.ModeRevision = 2;
	}
}
