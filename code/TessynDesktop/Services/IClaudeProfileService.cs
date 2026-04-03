using System.Threading.Tasks;

namespace TessynDesktop.Services;

/// <summary>
/// Queries Claude CLI profile authentication status and launches interactive auth.
/// Profiles are isolated via the CLAUDE_CONFIG_DIR environment variable.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IClaudeProfileService
{
	/// <summary>
	/// Returns the absolute path to the profiles root directory
	/// (e.g. %APPDATA%\TessynDesktop\profiles\).
	/// </summary>
	string ProfilesRootDirectory { get; }

	/// <summary>
	/// Runs <c>claude auth status</c> with the given config directory
	/// (via CLAUDE_CONFIG_DIR env var) and returns the account email,
	/// or null if not authenticated or the command fails.
	/// Pass null for the default profile (no env var override).
	/// </summary>
	Task<string?> GetAccountEmailAsync(string claudePath, string? configDir);

	/// <summary>
	/// Launches <c>claude auth login</c> in a visible console window
	/// with CLAUDE_CONFIG_DIR set to the given directory.
	/// Waits for the process to exit.
	/// </summary>
	Task LaunchAuthLoginAsync(string claudePath, string configDir);
}
