using System;
using System.IO;
using TessynDesktop;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class SessionFileServiceTests : IDisposable
{
	private readonly string _tempDir;
	private readonly SessionFileService _sut;

	public SessionFileServiceTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_sessions_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);

		var settings = new AppSettingsService(_tempDir);
		settings.Settings.SessionFilesRoot = _tempDir;
		_sut = new SessionFileService(settings);
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	[Fact]
	public void CreateSessionFile_ReturnsFileNameMatchingPattern_AndFileExists()
	{
		var fileName = _sut.CreateSessionFile();

		Assert.True(File.Exists(Path.Combine(_tempDir, fileName)));
		Assert.EndsWith(Constants.SessionFileExtension, fileName);
		// Pattern: YYYY-MM-dd-HHmm-xxxxxx.txt  (length: 10+1+4+1+6+4 = 26 chars)
		Assert.Matches(@"^\d{4}-\d{2}-\d{2}-\d{4}-[a-z]{6}\.txt$", fileName);
	}

	[Fact]
	public void AppendMessage_ThenReadEntries_RoundTripsRoleAndContent()
	{
		var fileName = _sut.CreateSessionFile();
		_sut.AppendMessage(fileName, Constants.SessionFile.RoleUser, "Hello Claude");
		_sut.AppendMessage(fileName, Constants.SessionFile.RoleAssistant, "Hello! How can I help?");

		var entries = _sut.ReadEntries(fileName);

		Assert.Equal(2, entries.Count);
		Assert.Equal(Constants.SessionFile.RoleUser, entries[0].Role);
		Assert.Equal("Hello Claude", entries[0].Content);
		Assert.Equal(Constants.SessionFile.RoleAssistant, entries[1].Role);
		Assert.Equal("Hello! How can I help?", entries[1].Content);
	}

	[Fact]
	public void AppendCompactionSeparator_ProducesCompactionEntry()
	{
		var fileName = _sut.CreateSessionFile();
		_sut.AppendMessage(fileName, Constants.SessionFile.RoleUser, "Before compaction");
		_sut.AppendCompactionSeparator(fileName);
		_sut.AppendMessage(fileName, Constants.SessionFile.RoleAssistant, "After compaction");

		var entries = _sut.ReadEntries(fileName);

		Assert.Equal(3, entries.Count);
		Assert.False(entries[0].IsCompaction);
		Assert.True(entries[1].IsCompaction);
		Assert.Equal(string.Empty, entries[1].Content);
		Assert.False(entries[2].IsCompaction);
		Assert.Equal("After compaction", entries[2].Content);
	}

	[Fact]
	public void AppendMessage_TimestampIsRecorded()
	{
		var before = DateTimeOffset.UtcNow.AddSeconds(-1);
		var fileName = _sut.CreateSessionFile();
		_sut.AppendMessage(fileName, Constants.SessionFile.RoleUser, "test");
		var after = DateTimeOffset.UtcNow.AddSeconds(1);

		var entries = _sut.ReadEntries(fileName);

		Assert.Single(entries);
		Assert.True(entries[0].Timestamp >= before);
		Assert.True(entries[0].Timestamp <= after);
	}

	[Fact]
	public void AppendMessage_MultiLineContent_RoundTrips()
	{
		var fileName = _sut.CreateSessionFile();
		var multiline = "Line one\nLine two\nLine three";
		_sut.AppendMessage(fileName, Constants.SessionFile.RoleAssistant, multiline);

		var entries = _sut.ReadEntries(fileName);

		Assert.Single(entries);
		Assert.Equal("Line one\r\nLine two\r\nLine three", entries[0].Content, ignoreLineEndingDifferences: true);
	}

	[Fact]
	public void SessionFileExists_ReturnsTrueForCreatedFile_FalseForMissing()
	{
		var fileName = _sut.CreateSessionFile();
		Assert.True(_sut.SessionFileExists(fileName));
		Assert.False(_sut.SessionFileExists("nonexistent-2026-01-01-0000-aaaaaa.txt"));
	}

	[Fact]
	public void WriteSessionFile_WritesEntries_ThenReadReturnsAll()
	{
		var fileName = _sut.CreateSessionFile();
		var entries = new[]
		{
			new SessionEntryModel
			{
				Timestamp = new DateTimeOffset(2026, 3, 10, 14, 30, 0, TimeSpan.Zero),
				Role = Constants.SessionFile.RoleUser,
				Content = "Hello Claude",
			},
			new SessionEntryModel
			{
				Timestamp = new DateTimeOffset(2026, 3, 10, 14, 30, 5, TimeSpan.Zero),
				Role = Constants.SessionFile.RoleAssistant,
				Content = "Hello! How can I help?",
			},
		};

		_sut.WriteSessionFile(fileName, entries);

		var readBack = _sut.ReadEntries(fileName);
		Assert.Equal(2, readBack.Count);
		Assert.Equal(Constants.SessionFile.RoleUser, readBack[0].Role);
		Assert.Equal("Hello Claude", readBack[0].Content);
		Assert.Equal(Constants.SessionFile.RoleAssistant, readBack[1].Role);
		Assert.Equal("Hello! How can I help?", readBack[1].Content);
	}

	[Fact]
	public void WriteSessionFile_WithCompactionEntry_RoundTrips()
	{
		var fileName = _sut.CreateSessionFile();
		var entries = new[]
		{
			new SessionEntryModel
			{
				Timestamp = new DateTimeOffset(2026, 3, 10, 14, 0, 0, TimeSpan.Zero),
				Role = Constants.SessionFile.RoleUser,
				Content = "Before compaction",
			},
			new SessionEntryModel
			{
				Timestamp = new DateTimeOffset(2026, 3, 10, 14, 5, 0, TimeSpan.Zero),
				Role = Constants.SessionFile.RoleCompaction,
				Content = string.Empty,
			},
			new SessionEntryModel
			{
				Timestamp = new DateTimeOffset(2026, 3, 10, 14, 10, 0, TimeSpan.Zero),
				Role = Constants.SessionFile.RoleAssistant,
				Content = "After compaction",
			},
		};

		_sut.WriteSessionFile(fileName, entries);

		var readBack = _sut.ReadEntries(fileName);
		Assert.Equal(3, readBack.Count);
		Assert.Equal(Constants.SessionFile.RoleUser, readBack[0].Role);
		Assert.True(readBack[1].IsCompaction);
		Assert.Equal(Constants.SessionFile.RoleAssistant, readBack[2].Role);
		Assert.Equal("After compaction", readBack[2].Content);
	}
}
