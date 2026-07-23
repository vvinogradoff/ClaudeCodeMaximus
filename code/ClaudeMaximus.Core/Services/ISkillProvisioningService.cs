namespace ClaudeMaximus.Services;

/// <summary>
/// Provisions ClaudeMaximus-authored skills into every profile's Claude Code config directory
/// so any session — regardless of which profile or project it runs under — can discover and
/// pull them in on demand (FR.15.11).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISkillProvisioningService
{
	/// <summary>
	/// Writes the orchestration skill's SKILL.md into every profile's skills folder, creating
	/// directories as needed. Idempotent — only rewrites a file when its content actually
	/// differs, mirroring <c>AgentMcpServer.EnsureConfigFileAsync</c>'s write-if-changed pattern.
	/// No-op when <c>AgentToolsEnabled</c> is off (the skill is about MCP tools that aren't
	/// registered in that case).
	/// </summary>
	void ProvisionAll();
}
