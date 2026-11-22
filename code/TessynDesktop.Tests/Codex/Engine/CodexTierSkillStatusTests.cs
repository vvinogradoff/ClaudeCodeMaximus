using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Codex.Fakes;
using TessynDesktop.Tests.Services;
using Xunit;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>GPT tiers (FR.33.14, TC.CX.24), the Codex skill (FR.33.16, TC.CX.35) and the Codex status and path (FR.33.17, TC.CX.36).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexTierSkillStatusTests : IAsyncLifetime
{
	private readonly CodexServicesFixture _f = new();

	public async Task InitializeAsync() => await _f.LoadCatalogAsync();

	public async Task DisposeAsync() => await _f.DisposeAsync();

	[Fact]
	public void TC_CX_24_the_default_table_puts_astra_at_Opus_and_sol_at_Sonnet()
	{
		var t = _f.Tiers;

		Assert.True(t.IsAllowed(FixedClaudeModels.Opus, "gpt-x-sol"));
		Assert.True(t.IsAllowed(FixedClaudeModels.Opus, "gpt-x-astra"));
		Assert.True(t.IsAllowed(FixedClaudeModels.Sonnet, "gpt-x-sol"));
		Assert.False(t.IsAllowed(FixedClaudeModels.Sonnet, "gpt-x-astra"));
		Assert.True(t.IsAllowed("gpt-x-sol", FixedClaudeModels.Sonnet));
		Assert.False(t.IsAllowed("gpt-x-sol", FixedClaudeModels.Opus));
		Assert.True(t.IsAllowed("gpt-x-astra", FixedClaudeModels.Opus));
		Assert.False(t.IsAllowed("gpt-x-astra", FixedClaudeModels.Fable));
		Assert.Equal((1, 2, 0), (t.GetTier("gpt-x-luna"), t.GetTier("gpt-unknown"), t.GetTier(FixedClaudeModels.Local)));
	}

	[Fact]
	public void TC_CX_24_an_edited_table_applies_from_the_next_call_and_reset_brings_the_defaults_back()
	{
		var settings = new CodexSettingsService(_f.Settings, new CodexExecutableLocator(new FakeCodexLocatorEnvironment()));

		settings.SetModelTiers([new CodexModelTierModel { Match = "astra", Tier = 4 }, new CodexModelTierModel { Match = " ", Tier = 9 }]);
		Assert.False(_f.Tiers.IsAllowed(FixedClaudeModels.Opus, "gpt-x-astra"));
		Assert.Equal(2, _f.Tiers.GetTier("gpt-x-sol")); // no row: the unmatched default
		Assert.Single(settings.GetModelTiers());

		settings.ResetModelTiers();
		Assert.True(_f.Tiers.IsAllowed(FixedClaudeModels.Opus, "gpt-x-astra"));
		Assert.Null(_f.Settings.Settings.CodexModelTiers);
		Assert.Equal(["astra", "sol", "terra", "luna", "gpt"], settings.GetModelTiers().Select(r => r.Match));
	}

	[Fact]
	public void TC_CX_35_the_skill_is_written_into_stored_profiles_only_with_the_Codex_wording_and_rewritten_only_when_it_changed()
	{
		_f.AddStoredProfile("codex_2", "b@example.com");
		_f.Settings.Settings.AgentToolsEnabled = true;
		var system = Path.Combine(_f.StoredRoot, "codex_1", "skills", ".system", "imagegen");
		Directory.CreateDirectory(system);
		File.WriteAllText(Path.Combine(system, "SKILL.md"), "codex's own");
		var service = new SkillProvisioningService(_f.Settings, new FakeProfileServiceForTurnService(), _f.Profiles);

		service.ProvisionAll();

		foreach (var id in new[] { "codex_1", "codex_2" })
		{
			var path = Path.Combine(_f.StoredRoot, id, "skills", "cmx-orchestration", "SKILL.md");
			Assert.Equal(Constants.Agent.CodexOrchestrationSkillMarkdown, File.ReadAllText(path));
			Assert.DoesNotContain("ScheduleWakeup", File.ReadAllText(path));
		}
		Assert.False(Directory.Exists(Path.Combine(_f.DefaultHome, "skills")));
		Assert.Equal("codex's own", File.ReadAllText(Path.Combine(system, "SKILL.md")));

		var skill   = Path.Combine(_f.StoredRoot, "codex_1", "skills", "cmx-orchestration", "SKILL.md");
		var written = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		File.SetLastWriteTimeUtc(skill, written);
		service.ProvisionAll();
		Assert.Equal(written, File.GetLastWriteTimeUtc(skill));

		File.WriteAllText(skill, "edited");
		service.ProvisionAll();
		Assert.Equal(Constants.Agent.CodexOrchestrationSkillMarkdown, File.ReadAllText(skill));
	}

	[Fact]
	public async Task TC_CX_36_a_missing_Codex_gives_the_install_status_line_and_a_missing_configured_path_names_it()
	{
		var locator  = new CodexExecutableLocator(new FakeCodexLocatorEnvironment());
		var settings = new CodexSettingsService(_f.Settings, locator);

		var missing = await settings.GetStatusAsync();
		Assert.False(missing.Found);
		Assert.Equal("Codex not found. Install it with: winget install OpenAI.Codex", missing.StatusLine);
		Assert.Equal(Constants.Codex.TestedVersion, missing.TestedVersion);

		settings.SetCodexPath(@"C:\nowhere\codex.exe");
		var configured = await settings.GetStatusAsync();
		Assert.Equal(@"The Codex path C:\nowhere\codex.exe does not exist.", configured.StatusLine);
		Assert.Equal(@"C:\nowhere\codex.exe", _f.Settings.Settings.CodexPath);
	}

	[Fact]
	public void TC_CX_36_hosts_are_launched_from_the_configured_path()
	{
		var env = new FakeCodexLocatorEnvironment();
		env.Files.Add(@"D:\tools\codex\codex.exe");
		var launcher = new FakeCodexProcessLauncher();
		var pool     = new CodexHostPool(launcher, new CodexExecutableLocator(env), _f.Clock, appSettings: _f.Settings);
		_f.Settings.Settings.CodexPath = @"D:\tools\codex\codex.exe";

		var host = pool.GetHost(new CodexProfileRef("codex-default", null));
		_f.Settings.Settings.CodexPath = @"D:\missing\codex.exe";

		Assert.NotNull(host);
		Assert.Throws<CodexNotFoundException>(() => pool.GetHost(new CodexProfileRef("codex_9", @"C:\h")));
	}
}
