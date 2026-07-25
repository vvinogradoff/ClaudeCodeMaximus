using System.IO;
using Serilog;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class SkillProvisioningService : ISkillProvisioningService
{
	private static readonly ILogger _log = Log.ForContext<SkillProvisioningService>();

	private readonly IAppSettingsService  _appSettings;
	private readonly IClaudeProfileService _profileService;

	public SkillProvisioningService(IAppSettingsService appSettings, IClaudeProfileService profileService)
	{
		_appSettings    = appSettings;
		_profileService = profileService;
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

			ProvisionOne(configDir, profiles[i].DisplayName);
		}
	}

	private static void ProvisionOne(string configDir, string profileDisplayName)
	{
		var skillDir = Path.Combine(configDir, Constants.Agent.SkillsFolderName, Constants.Agent.OrchestrationSkillName);

		try
		{
			Directory.CreateDirectory(skillDir);

			var path    = Path.Combine(skillDir, Constants.Agent.SkillFileName);
			var content = Constants.Agent.OrchestrationSkillMarkdown;

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
