using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Models;
using Xunit;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>
/// The GPT catalog (FR.33.2): the merged order and tags (TC.CX.03), the cache (TC.CX.04), efforts per model (TC.CX.05), the engine that follows
/// the model (TC.CX.01), and no GPT entries without Codex (TC.CX.36).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexCatalogTests : IAsyncLifetime
{
	private readonly CodexServicesFixture _f = new();

	public Task InitializeAsync() => Task.CompletedTask;

	public async Task DisposeAsync() => await _f.DisposeAsync();

	[Fact]
	public async Task TC_CX_03_GPT_models_come_from_every_page_without_hidden_ones_tagged_codex_and_OpenAI_between_Anthropic_and_Ollama()
	{
		_f.Launcher.Configure = server =>
		{
			var hidden = server.Scripts.Model("gpt-reserve", "Reserve", false, "low", "low");
			hidden["hidden"] = true;
			server.ModelPages =
			[
				new JsonArray(server.Scripts.Model("gpt-x-astra", "GPT-X-Astra", false, "low", "low", "high"), hidden),
				new JsonArray(server.Scripts.Model("gpt-x-sol", "GPT-X-Sol", true, "low", "low", "medium"), server.Scripts.Model("gpt-x-luna", "GPT-X-Luna", false, "medium", "low", "medium")),
			];
		};

		await _f.LoadCatalogAsync();

		var models = _f.Models.GetModels();
		Assert.Equal(
			[FixedClaudeModels.Fable, FixedClaudeModels.Opus, FixedClaudeModels.Sonnet, FixedClaudeModels.Haiku, "gpt-x-sol", "gpt-x-astra", "gpt-x-luna", FixedClaudeModels.Local],
			models.Select(m => m.Id));
		Assert.All(models.Where(m => m.Id.StartsWith("gpt-")), m => Assert.Equal((Constants.SessionEngines.Codex, ModelProvider.OpenAI), (m.EngineId, m.Provider)));
		Assert.All(models.Where(m => !m.Id.StartsWith("gpt-")), m => Assert.Equal(Constants.SessionEngines.Claude, m.EngineId));
		Assert.Equal(ModelProvider.Ollama, models.Last().Provider);
		Assert.True(models.Single(m => m.Id == "gpt-x-sol").IsDefault);
		Assert.Equal(2, _f.Requests("model/list").Length);
		Assert.All(_f.Requests("model/list"), r => Assert.False(r.GetProperty("includeHidden").GetBoolean()));
		Assert.Null(_f.Models.GptHintLine);
	}

	[Fact]
	public async Task TC_CX_04_a_fresh_cache_is_used_without_asking_Codex()
	{
		_f.WriteCache(_f.Clock.GetUtcNow().AddHours(-23), CodexServicesFixture.Cached("gpt-cached-sol", true, "low"));

		await _f.Catalog.StartAsync();

		Assert.Empty(_f.Launcher.Servers);
		Assert.Equal(["gpt-cached-sol"], _f.Catalog.Models.Select(m => m.Id));
		Assert.True(_f.Models.IsGpt("gpt-cached-sol"));
	}

	[Fact]
	public async Task TC_CX_04_a_cache_older_than_24_hours_is_refreshed_at_start_and_written_back()
	{
		_f.WriteCache(_f.Clock.GetUtcNow().AddHours(-25), CodexServicesFixture.Cached("gpt-cached-sol", true, "low"));
		_f.AddStoredProfile("codex_1", "u@example.com");

		await _f.Catalog.StartAsync();

		Assert.Equal(["gpt-x-sol", "gpt-x-astra", "gpt-x-luna"], _f.Catalog.Models.Select(m => m.Id));
		Assert.Contains("gpt-x-astra", File.ReadAllText(_f.CachePath));
		Assert.Equal(_f.Clock.GetUtcNow(), _f.Catalog.FetchedAtUtc);
	}

	[Fact]
	public async Task TC_CX_04_a_failed_fetch_keeps_the_cached_list()
	{
		_f.WriteCache(_f.Clock.GetUtcNow().AddDays(-3), CodexServicesFixture.Cached("gpt-cached-sol", true, "low"));
		_f.AddStoredProfile("codex_1", "u@example.com");
		_f.Launcher.Configure = server => server.On("model/list", call => call.ErrorAsync(-32603, "upstream unavailable"));

		await _f.Catalog.StartAsync();

		Assert.Equal(["gpt-cached-sol"], _f.Catalog.Models.Select(m => m.Id));
		Assert.False(await _f.Catalog.RefreshAsync());
	}

	[Fact]
	public async Task TC_CX_04_with_no_cache_and_no_signed_in_profile_there_are_no_GPT_entries_and_the_picker_shows_the_hint()
	{
		_f.AddStoredProfile("codex_1", "u@example.com");
		_f.Launcher.Configure = server => server.SignedIn = false;

		await _f.Catalog.StartAsync();

		Assert.Empty(_f.Catalog.Models);
		Assert.DoesNotContain(_f.Models.GetModels(), m => m.EngineId == Constants.SessionEngines.Codex);
		Assert.Equal("GPT models: sign in to ChatGPT in Settings > Profiles", _f.Models.GptHintLine);
		Assert.Empty(_f.Requests("model/list")); // a signed-out profile also answers model/list, but is never asked
	}

	[Fact]
	public async Task The_catalog_profile_skips_a_signed_out_profile_and_takes_the_next_signed_in_one()
	{
		_f.AddStoredProfile("codex_1", "a@example.com");
		_f.AddStoredProfile("codex_2", "b@example.com");
		_f.Settings.Settings.CodexCatalogProfileId = "codex_1";
		_f.Launcher.Configure = server => server.SignedIn = server.Spec.CodexHome?.EndsWith("codex_2", StringComparison.Ordinal) == true;

		await _f.Catalog.RefreshAsync();

		Assert.Equal("codex_2", _f.Catalog.SourceProfileId);
		Assert.Equal(3, _f.Catalog.Models.Count);
	}

	[Fact]
	public async Task A_sign_in_refreshes_the_catalog()
	{
		var started = await _f.Profiles.BeginSignInAsync(deviceCode: false);
		await _f.ServerOf(Path.Combine(_f.StoredRoot, "codex_1")).NotifyAsync("account/login/completed",
			new JsonObject { ["loginId"] = started.LoginId, ["success"] = true, ["error"] = null });

		await _f.Eventually.TrueAsync(() => _f.Catalog.Models.Count == 3, "the catalog was refreshed after the sign-in");
	}

	[Fact]
	public async Task TC_CX_05_GPT_efforts_are_Codex_s_without_ultra_and_an_effort_the_model_lacks_is_sent_as_none()
	{
		await _f.LoadCatalogAsync();

		Assert.Equal(["low", "medium", "high", "xhigh", "max"], _f.Models.EffortsFor("gpt-x-sol").Select(e => e.Effort));
		Assert.Equal("low reasoning", _f.Models.EffortsFor("gpt-x-sol")[0].Description);
		Assert.Equal(["low", "medium"], _f.Models.EffortsFor("gpt-x-luna").Select(e => e.Effort));
		Assert.Equal(["max", "high", "medium", "low"], _f.Models.EffortsFor(FixedClaudeModels.Opus).Select(e => e.Effort));
		Assert.Null(_f.Models.EffectiveEffort("gpt-x-luna", "high"));
		Assert.Equal("medium", _f.Models.EffectiveEffort("gpt-x-luna", "MEDIUM"));
		Assert.Null(_f.Models.EffectiveEffort("gpt-x-sol", "default"));
		Assert.DoesNotContain(_f.Catalog.Models, m => m.Efforts.Any(e => e.Effort == "ultra"));
	}

	[Fact]
	public async Task TC_CX_01_the_engine_follows_the_model()
	{
		var node = new SessionNodeModel { Name = "S", FileName = "s.txt" };
		Assert.Null(_f.Selector.SelectEngineId(node, "gpt-x-sol")); // a GPT-looking id while the GPT list was never loaded: no answer

		await _f.LoadCatalogAsync();

		Assert.Equal("codex", _f.Selector.SelectEngineId(node, "gpt-x-sol"));
		Assert.Equal("codex", _f.Selector.SelectEngineId(node, "GPT-X-LUNA"));
		foreach (var claude in new[] { FixedClaudeModels.Opus, "default", null, FixedClaudeModels.Local, "not-in-any-catalog", "gpt-old-1" })
			Assert.Equal("claude", _f.Selector.SelectEngineId(node, claude));
	}

	[Fact]
	public async Task TC_CX_36_without_Codex_there_are_no_GPT_entries()
	{
		await using var pool = new CodexHostPool(new Fakes.FakeCodexProcessLauncher(), new CodexExecutableLocator(new Fakes.FakeCodexLocatorEnvironment()));
		var profiles = new CodexProfileService(pool, _f.Settings, _f.StoredRoot, _f.DefaultHome);
		var catalog  = new CodexModelCatalog(pool, profiles, _f.Clock, _f.Root);
		_f.AddStoredProfile("codex_1", "u@example.com");

		await catalog.StartAsync();

		Assert.Empty(catalog.Models);
		Assert.Equal(Constants.Codex.Catalog.SignInHint, new TessynDesktop.Services.SessionModelCatalog(_f.Claude, catalog).GptHintLine);
	}
}
