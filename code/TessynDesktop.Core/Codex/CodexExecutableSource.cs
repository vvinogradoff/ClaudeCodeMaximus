namespace TessynDesktop.Codex;

/// <summary>Where <see cref="CodexExecutableLocator"/> found Codex (FR.33.17).</summary>
/// <remarks>Created by Claude</remarks>
public enum CodexExecutableSource
{
	None,

	/// <summary>The Codex path set in Settings (<c>AppSettingsModel.CodexPath</c>).</summary>
	Configured,

	/// <summary><c>%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe</c>.</summary>
	OfficialInstaller,

	/// <summary>The winget link <c>%LOCALAPPDATA%\Microsoft\WinGet\Links\codex.exe</c>, resolved to its target.</summary>
	Winget,

	/// <summary>The native exe of a global npm install (never its <c>codex.cmd</c> or Node shim).</summary>
	Npm,

	/// <summary><c>codex.exe</c> on the PATH.</summary>
	Path,
}
