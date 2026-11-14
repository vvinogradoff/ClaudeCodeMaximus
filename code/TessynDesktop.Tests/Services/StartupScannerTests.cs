using System;
using System.IO;
using System.Linq;
using TessynDesktop;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Background scanners that replaced the UI-thread startup scans (ISSUE.016).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class StartupScannerTests : IDisposable
{
	private readonly string _tempDir;
	private readonly SessionFileService _files;
	private readonly DraftService _drafts;
	private readonly SessionLastActivityScanner _activity;

	public StartupScannerTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_startup_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
		var settings = new AppSettingsService(_tempDir);
		settings.Settings.SessionFilesRoot = _tempDir;
		_files = new SessionFileService(settings);
		_drafts = new DraftService(Path.Combine(_tempDir, "drafts"));
		_activity = new SessionLastActivityScanner(_files, _drafts);
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	[Fact]
	public void Scan_ReturnsLastUserPromptLastAssistantAndProfile()
	{
		var file = _files.CreateSessionFile();
		_files.AppendMessage(file, Constants.SessionFile.RoleUser, "first", profileName: "Old");
		_files.AppendMessage(file, Constants.SessionFile.RoleAssistant, "reply 1");
		_files.AppendMessage(file, Constants.SessionFile.RoleUser, "second", profileName: "Work");
		_files.AppendMessage(file, Constants.SessionFile.RoleAssistant, "reply 2");
		var entries = _files.ReadEntries(file);

		var result = Assert.Single(_activity.Scan([file]));

		Assert.Equal(file, result.FileName);
		Assert.Equal(entries[2].Timestamp, result.LastPromptTimestamp);
		Assert.Equal(entries[3].Timestamp, result.LastResponseTimestamp);
		Assert.Equal("Work", result.ProfileName);
		Assert.Null(result.LastDraftEditTimestamp);
	}

	[Fact]
	public void Scan_MissingFile_YieldsNullTimestamps_AndKeepsInputOrder()
	{
		var file = _files.CreateSessionFile();
		_files.AppendMessage(file, Constants.SessionFile.RoleUser, "hi");

		var results = _activity.Scan(["no-such-file.txt", file]);

		Assert.Equal(["no-such-file.txt", file], results.Select(r => r.FileName));
		Assert.Null(results[0].LastPromptTimestamp);
		Assert.Null(results[0].LastResponseTimestamp);
		Assert.NotNull(results[1].LastPromptTimestamp);
		Assert.Null(results[1].LastResponseTimestamp);
	}

	[Fact]
	public void Scan_RealDraft_ReturnsItsTimestamp_WhitespaceDraftIsIgnored()
	{
		var real = _files.CreateSessionFile();
		var blank = _files.CreateSessionFile();
		_drafts.SaveDraft(real, "unsent text");
		_drafts.SaveDraft(blank, "   \n ");

		var results = _activity.Scan([real, blank]);

		Assert.Equal(_drafts.GetDraftTimestamp(real), results[0].LastDraftEditTimestamp);
		Assert.Null(results[1].LastDraftEditTimestamp);
	}

	[Fact]
	public void GitOriginScan_ResolvesOriginPerPath_AndNullForNonRepoOrInvalidPath()
	{
		var repo = Path.Combine(_tempDir, "repo");
		Directory.CreateDirectory(Path.Combine(repo, ".git"));
		File.WriteAllText(Path.Combine(repo, ".git", "config"), "[remote \"origin\"]\n\turl = https://example.test/x.git\n");
		var plain = Path.Combine(_tempDir, "plain");
		Directory.CreateDirectory(plain);
		var sut = new GitOriginScanner(new GitOriginService());

		// The repo's sub-directory also resolves through the parent .git; "plain" sits under _tempDir, which has no .git.
		var results = sut.Scan([repo, Path.Combine(repo, "src"), plain, "\0bad"]);

		Assert.Equal("https://example.test/x.git", results[0].OriginUrl);
		Assert.Equal("https://example.test/x.git", results[1].OriginUrl);
		Assert.Null(results[2].OriginUrl);
		Assert.Null(results[3].OriginUrl);
		Assert.Equal(repo, results[0].Path);
	}
}
