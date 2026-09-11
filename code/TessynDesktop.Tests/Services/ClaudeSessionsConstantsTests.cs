using System;
using System.IO;
using TessynDesktop;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Covers <see cref="Constants.ClaudeSessions.FindExistingJsonlPath"/> — the cross-profile JSONL
/// lookup used so a session's live Claude CLI transcript keeps resolving after its originating
/// profile is removed (FR.12.18, renamed to a "_rem" directory rather than deleted).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClaudeSessionsConstantsTests : IDisposable
{
	private readonly string _tempRoot;
	private readonly string _defaultDir;
	private readonly string _activeProfileDir;
	private readonly string _removedProfileDir;

	public ClaudeSessionsConstantsTests()
	{
		_tempRoot = Path.Combine(Path.GetTempPath(), "cmx-claude-sessions-consts-" + Guid.NewGuid().ToString("N"));
		_defaultDir = Path.Combine(_tempRoot, "default", "projects");
		_activeProfileDir = Path.Combine(_tempRoot, "profile_1", "projects");
		_removedProfileDir = Path.Combine(_tempRoot, "profile_2_rem", "projects");

		Directory.CreateDirectory(_defaultDir);
		Directory.CreateDirectory(_activeProfileDir);
		Directory.CreateDirectory(_removedProfileDir);
	}

	public void Dispose() => Directory.Delete(_tempRoot, recursive: true);

	private void WriteJsonl(string projectsDir, string workingDirectory, string sessionId)
	{
		var slug = Constants.ClaudeSessions.BuildProjectSlug(workingDirectory);
		var slugDir = Path.Combine(projectsDir, slug);
		Directory.CreateDirectory(slugDir);
		File.WriteAllText(Path.Combine(slugDir, sessionId + Constants.ClaudeSessions.SessionFileExtension), "{}");
	}

	[Fact]
	public void FindExistingJsonlPath_FindsFile_UnderDefaultDir()
	{
		WriteJsonl(_defaultDir, @"C:\proj", "session-a");

		var result = Constants.ClaudeSessions.FindExistingJsonlPath(
			@"C:\proj", "session-a", [_defaultDir, _activeProfileDir, _removedProfileDir]);

		Assert.Equal(Path.Combine(_defaultDir, "C--proj", "session-a.jsonl"), result);
	}

	[Fact]
	public void FindExistingJsonlPath_FindsFile_UnderRemovedProfileDir()
	{
		// Simulates a session that only ever ran under a profile later removed (renamed "_rem").
		WriteJsonl(_removedProfileDir, @"C:\proj", "session-b");

		var result = Constants.ClaudeSessions.FindExistingJsonlPath(
			@"C:\proj", "session-b", [_defaultDir, _activeProfileDir, _removedProfileDir]);

		Assert.Equal(Path.Combine(_removedProfileDir, "C--proj", "session-b.jsonl"), result);
	}

	[Fact]
	public void FindExistingJsonlPath_ReturnsNull_WhenNotFoundAnywhere()
	{
		var result = Constants.ClaudeSessions.FindExistingJsonlPath(
			@"C:\proj", "session-missing", [_defaultDir, _activeProfileDir, _removedProfileDir]);

		Assert.Null(result);
	}
}
