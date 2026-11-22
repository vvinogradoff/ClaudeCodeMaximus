using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Codex;

// Copied from TessynDesktop.Tests/Codex/Fakes for the Tessyn Web E2E harness (FR.33, TC.CX.42): this project cannot
// reference the test project, so the in-process fake Codex app-server is compiled here too. Keep the two copies in step.
namespace Tessyn.Web.E2E.Tests.Harness.Codex;

/// <summary>An in-memory file system and environment for <see cref="CodexExecutableLocator"/> tests.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeCodexLocatorEnvironment : ICodexLocatorEnvironment
{
	public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

	public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Symbolic link to final target.</summary>
	public Dictionary<string, string> Links { get; } = new(StringComparer.OrdinalIgnoreCase);

	public Dictionary<string, string?> VersionOutputs { get; } = new(StringComparer.OrdinalIgnoreCase);

	public string? GetEnvironmentVariable(string name) => Variables.TryGetValue(name, out var value) ? value : null;

	public bool FileExists(string path) => Files.Contains(path);

	public bool DirectoryExists(string path) => Files.Any(f => f.StartsWith(path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));

	public string? ResolveLinkTarget(string path) => Links.TryGetValue(path, out var target) ? target : null;

	public IEnumerable<string> FindFiles(string directory, string fileName) =>
		Files.Where(f => f.StartsWith(directory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
			&& string.Equals(System.IO.Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase)).ToList();

	public Task<string?> ReadVersionOutputAsync(string executablePath, CancellationToken cancellationToken) =>
		Task.FromResult(VersionOutputs.TryGetValue(executablePath, out var output) ? output : null);
}
