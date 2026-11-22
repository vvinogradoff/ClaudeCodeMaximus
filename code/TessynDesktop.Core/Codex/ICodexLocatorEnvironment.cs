using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TessynDesktop.Codex;

/// <summary>The file system, environment and process access of <see cref="CodexExecutableLocator"/>, so discovery can be tested without a Codex install.</summary>
/// <remarks>Created by Claude</remarks>
public interface ICodexLocatorEnvironment
{
	string? GetEnvironmentVariable(string name);

	bool FileExists(string path);

	bool DirectoryExists(string path);

	/// <summary>The final target of a symbolic link, or null when <paramref name="path"/> is not a link.</summary>
	string? ResolveLinkTarget(string path);

	/// <summary>Files named <paramref name="fileName"/> anywhere below <paramref name="directory"/>.</summary>
	IEnumerable<string> FindFiles(string directory, string fileName);

	/// <summary>Runs <c>&lt;exe&gt; --version</c> hidden and returns its first output line, or null when it fails or times out.</summary>
	Task<string?> ReadVersionOutputAsync(string executablePath, CancellationToken cancellationToken);
}
