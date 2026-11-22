using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TessynDesktop.Codex;

/// <summary>
/// Finds the native Codex exe (FR.33.17). With a configured path, only that path is used. Otherwise, in order: the official installer
/// (<c>%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe</c>), the winget link (<c>%LOCALAPPDATA%\Microsoft\WinGet\Links\codex.exe</c>),
/// the native exe of a global npm install, then <c>codex.exe</c> on the PATH. A symbolic link is resolved to its target, which is
/// what is launched. A <c>.cmd</c>, <c>.ps1</c> or Node shim is never returned. Also reads and checks <c>codex --version</c> (FR.33.6).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexExecutableLocator
{
	private readonly ICodexLocatorEnvironment _environment;

	public CodexExecutableLocator(ICodexLocatorEnvironment? environment = null)
	{
		_environment = environment ?? new CodexLocatorEnvironment();
	}

	public CodexLocateResult Locate(string? configuredPath)
	{
		if (!string.IsNullOrWhiteSpace(configuredPath))
			return LocateConfigured(configuredPath.Trim());

		foreach (var (source, path) in Candidates())
		{
			if (TryUse(path, source) is { } found)
				return found;
		}

		return CodexLocateResult.NotFound(Constants.Codex.Texts.NotFound);
	}

	/// <summary>Runs <c>codex --version</c> and parses it; null when it cannot be run or read.</summary>
	public async Task<Version?> ReadVersionAsync(string executablePath, CancellationToken cancellationToken = default)
	{
		var output = await _environment.ReadVersionOutputAsync(executablePath, cancellationToken).ConfigureAwait(false);
		return ParseVersion(output);
	}

	/// <summary>Parses <c>codex-cli 0.171.0</c> (or a bare <c>0.171.0</c>, or one with a pre-release suffix such as <c>0.172.0-alpha.1</c>).</summary>
	public Version? ParseVersion(string? versionOutput)
	{
		if (string.IsNullOrWhiteSpace(versionOutput))
			return null;

		var text = versionOutput.Trim();
		if (text.StartsWith(Constants.Codex.Discovery.VersionOutputPrefix, StringComparison.OrdinalIgnoreCase))
			text = text[Constants.Codex.Discovery.VersionOutputPrefix.Length..].Trim();

		var token = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
		token     = token.TrimStart('v', 'V');
		var cut   = token.IndexOfAny(['-', '+']);
		if (cut >= 0)
			token = token[..cut];

		return Version.TryParse(token, out var version) ? version : null;
	}

	/// <summary>True when the version matches <see cref="Constants.Codex.TestedVersion"/> on major and minor.</summary>
	public bool IsTestedVersion(Version version)
	{
		var tested = Version.Parse(Constants.Codex.TestedVersion);
		return version.Major == tested.Major && version.Minor == tested.Minor;
	}

	/// <summary>The Settings note for a version other than the tested one (FR.33.6), else null. It never blocks.</summary>
	public string? VersionNote(Version version) =>
		IsTestedVersion(version) ? null : string.Format(Constants.Codex.Texts.UntestedVersionFormat, version.ToString(3), Constants.Codex.TestedVersion);

	/// <summary>
	/// The Settings > General status line (FR.33.17): <c>Codex 0.171.0 found at &lt;path&gt;</c>, followed by the version note when the version is
	/// not the tested one, or the reason nothing was found.
	/// </summary>
	public string StatusLine(CodexLocateResult result, Version? version)
	{
		if (!result.Found)
			return result.Problem ?? Constants.Codex.Texts.NotFound;

		var found = string.Format(Constants.Codex.Texts.FoundFormat, version?.ToString(3) ?? "(version unknown)", result.ExecutablePath);
		return version is not null && VersionNote(version) is { } note ? found + " " + note : found;
	}

	private CodexLocateResult LocateConfigured(string path)
	{
		if (IsShim(path))
			return CodexLocateResult.NotFound(string.Format(Constants.Codex.Texts.ConfiguredPathShimFormat, path));

		return TryUse(path, CodexExecutableSource.Configured)
			?? CodexLocateResult.NotFound(string.Format(Constants.Codex.Texts.ConfiguredPathMissingFormat, path));
	}

	private IEnumerable<(CodexExecutableSource Source, string Path)> Candidates()
	{
		var localAppData = _environment.GetEnvironmentVariable(Constants.Codex.Discovery.LocalAppDataVariable);
		if (!string.IsNullOrEmpty(localAppData))
		{
			yield return (CodexExecutableSource.OfficialInstaller, Path.Combine(localAppData, Constants.Codex.Discovery.OfficialInstallerRelativePath));
			yield return (CodexExecutableSource.Winget, Path.Combine(localAppData, Constants.Codex.Discovery.WingetLinkRelativePath));
		}

		var appData = _environment.GetEnvironmentVariable(Constants.Codex.Discovery.AppDataVariable);
		if (!string.IsNullOrEmpty(appData))
		{
			foreach (var relative in Constants.Codex.Discovery.NpmNativePackageRelativePaths)
			{
				var folder = Path.Combine(appData, relative);
				if (!_environment.DirectoryExists(folder))
					continue;

				foreach (var exe in _environment.FindFiles(folder, Constants.Codex.Discovery.ExecutableFileName).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
					yield return (CodexExecutableSource.Npm, exe);
			}
		}

		var pathVariable = _environment.GetEnvironmentVariable(Constants.Codex.Discovery.PathVariable) ?? string.Empty;
		foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			string candidate;
			try
			{
				candidate = Path.Combine(directory.Trim('"'), Constants.Codex.Discovery.ExecutableFileName);
			}
			catch (ArgumentException)
			{
				continue;
			}
			yield return (CodexExecutableSource.Path, candidate);
		}
	}

	private CodexLocateResult? TryUse(string path, CodexExecutableSource source)
	{
		if (!_environment.FileExists(path))
			return null;

		var target = _environment.ResolveLinkTarget(path);
		var launch = target is not null && _environment.FileExists(target) ? target : path;

		if (IsShim(launch) || !string.Equals(Path.GetExtension(launch), Constants.Codex.Launch.ExecutableExtension, StringComparison.OrdinalIgnoreCase))
			return null;

		return new CodexLocateResult(launch, path, source, null);
	}

	private bool IsShim(string path) =>
		Constants.Codex.Launch.ShimExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
