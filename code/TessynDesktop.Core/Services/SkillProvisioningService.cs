using System.IO;
using System.Linq;
using Serilog;
using TessynDesktop.Codex;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class SkillProvisioningService : ISkillProvisioningService
{
	private static readonly ILogger _log = Log.ForContext<SkillProvisioningService>();

	private readonly IAppSettingsService  _appSettings;
	private readonly IClaudeProfileService _profileService;
	private readonly ICodexProfileService? _codexProfiles;

	/// <param name="codexProfiles">FR.33.16: the stored ChatGPT profiles also get the skill, with the Codex wording; a new sign-in gets it at once.</param>
	public SkillProvisioningService(IAppSettingsService appSettings, IClaudeProfileService profileService, ICodexProfileService? codexProfiles = null)
	{
		_appSettings    = appSettings;
		_profileService = profileService;
		_codexProfiles  = codexProfiles;
		if (_codexProfiles is not null)
			_codexProfiles.SignedIn += ProvisionCodex;
	}

	public void ProvisionAll()
	{
		if (!_appSettings.Settings.AgentToolsEnabled)
			return;

		var profiles = _appSettings.Settings.Profiles;
		for (var i = 0; i < profiles.Count; i++)
		{
			// profileIndex is 1-based (0 = Default, has no config dir of its own — skipped).
			var configDir = _profileService.GetConfigDirForProfile(i + 1, profiles);
			if (configDir == null)
				continue;

			ProvisionOne(configDir, profiles[i].DisplayName, Constants.Agent.OrchestrationSkillMarkdown);
		}

		// FR.33.16: every stored Codex profile (CODEX_HOME\skills\<name>\SKILL.md, a "user" skill). Codex Default is skipped, as Claude's Default
		// is, and Codex's own skills\.system folder is never touched.
		foreach (var profile in _codexProfiles?.List().Where(p => !p.IsDefault) ?? [])
			ProvisionOne(profile.Home, profile.DisplayName, Constants.Agent.CodexOrchestrationSkillMarkdown);
	}

	private void ProvisionCodex(string profileId)
	{
		if (!_appSettings.Settings.AgentToolsEnabled || _codexProfiles is null)
			return;

		var profile = _codexProfiles.List().FirstOrDefault(p => p.ProfileId == profileId && !p.IsDefault);
		if (profile is not null)
			ProvisionOne(profile.Home, profile.DisplayName, Constants.Agent.CodexOrchestrationSkillMarkdown);
	}

	private void ProvisionOne(string configDir, string profileDisplayName, string content)
	{
		var skillDir = Path.Combine(configDir, Constants.Agent.SkillsFolderName, Constants.Agent.OrchestrationSkillName);

		try
		{
			Directory.CreateDirectory(skillDir);

			var path    = Path.Combine(skillDir, Constants.Agent.SkillFileName);

			// Re-write only if missing or changed — avoids touching mtimes on every startup.
			if (!File.Exists(path) || File.ReadAllText(path) != content)
			{
				File.WriteAllText(path, content, System.Text.Encoding.UTF8);
				_log.Information("Provisioned {Skill} skill for profile {Profile}",
					Constants.Agent.OrchestrationSkillName, profileDisplayName);
			}
		}
		catch (IOException ex)
		{
			_log.Warning(ex, "Failed to provision {Skill} skill for profile {Profile}",
				Constants.Agent.OrchestrationSkillName, profileDisplayName);
		}
	}
}
