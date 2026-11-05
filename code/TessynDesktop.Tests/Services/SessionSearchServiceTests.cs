using System;
using System.IO;
using TessynDesktop;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class SessionSearchServiceTests : IDisposable
{
	private readonly string _tempDir;
	private readonly SessionFileService _files;
	private readonly SessionSearchService _sut;

	public SessionSearchServiceTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_search_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);

		var settings = new AppSettingsService(_tempDir);
		settings.Settings.SessionFilesRoot = _tempDir;
		_files = new SessionFileService(settings);
		_sut = new SessionSearchService(settings, _files);
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	[Fact]
	public void FindMatchingSessionFiles_DoesNotMatchHeaderLines_ButMatchesBodies()
	{
		var headerOnly = _files.CreateSessionFile();
		_files.AppendMessage(headerOnly, Constants.SessionFile.RoleUser, "plain question", profileName: "me@example.com");
		var body = _files.CreateSessionFile();
		_files.AppendMessage(body, Constants.SessionFile.RoleUser, "write to me@example.com today");

		var matches = _sut.FindMatchingSessionFiles("me@example.com");

		Assert.Equal([body], matches);
	}

	[Fact]
	public void FindMatchingSessionFiles_ReadsFile_HeldOpenForWriting()
	{
		var fileName = _files.CreateSessionFile();
		_files.AppendMessage(fileName, Constants.SessionFile.RoleUser, "needle in haystack");

		using var writer = new FileStream(_files.GetFullPath(fileName), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);

		var matches = _sut.FindMatchingSessionFiles("needle");

		Assert.Contains(fileName, matches);
	}
}
