using System;
using System.IO;
using TessynDesktop;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude. Tail read of the last-used profile/model/effort (ISSUE.016).</remarks>
public sealed class LastUsedSessionSettingsReaderTests : IDisposable
{
	private readonly string _tempDir;
	private readonly SessionFileService _files;

	public LastUsedSessionSettingsReaderTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_lastused_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
		var settings = new AppSettingsService(_tempDir);
		settings.Settings.SessionFilesRoot = _tempDir;
		_files = new SessionFileService(settings);
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	private static string Filler(int chars) => new string('x', chars);

	[Fact]
	public void MissingOrEmptyFile_ReturnsNothing()
	{
		var empty = _files.CreateSessionFile();

		Assert.Equal(new LastUsedSessionSettings(null, null, null), LastUsedSessionSettingsReader.Read(_files, empty));
		Assert.Equal(new LastUsedSessionSettings(null, null, null), LastUsedSessionSettingsReader.Read(_files, "does-not-exist.txt"));
	}

	[Fact]
	public void SmallFile_ReturnsNewestValues()
	{
		var f = _files.CreateSessionFile();
		_files.AppendMessage(f, Constants.SessionFile.RoleUser, "one", profileName: "Old");
		_files.AppendMessage(f, Constants.SessionFile.RoleAssistant, "a", modelId: "opus-old", effort: "low");
		_files.AppendMessage(f, Constants.SessionFile.RoleUser, "two", profileName: "New");
		_files.AppendMessage(f, Constants.SessionFile.RoleAssistant, "b", modelId: "sonnet-new", effort: "high");

		var result = LastUsedSessionSettingsReader.Read(_files, f);

		Assert.Equal(new LastUsedSessionSettings("New", "sonnet-new", "high"), result);
	}

	[Fact]
	public void NewestEntriesWithoutValues_AreSkipped()
	{
		var f = _files.CreateSessionFile();
		_files.AppendMessage(f, Constants.SessionFile.RoleUser, "one", profileName: "P");
		_files.AppendMessage(f, Constants.SessionFile.RoleAssistant, "a", modelId: "m1", effort: "medium");
		_files.AppendMessage(f, Constants.SessionFile.RoleUser, "no profile");
		_files.AppendMessage(f, Constants.SessionFile.RoleAssistant, "no model");

		Assert.Equal(new LastUsedSessionSettings("P", "m1", "medium"), LastUsedSessionSettingsReader.Read(_files, f));
	}

	[Fact]
	public void ValuesOutsideTheFirstWindow_AreFoundByWidening()
	{
		var f = _files.CreateSessionFile();
		_files.AppendMessage(f, Constants.SessionFile.RoleUser, "start", profileName: "Early");
		_files.AppendMessage(f, Constants.SessionFile.RoleAssistant, "start", modelId: "early-model", effort: "low");
		// ~300 KB of entries that carry neither profile nor model pushes the values past the 64 KB and 256 KB windows
		for (var i = 0; i < 30; i++)
		{
			_files.AppendMessage(f, Constants.SessionFile.RoleUser, Filler(5_000));
			_files.AppendMessage(f, Constants.SessionFile.RoleAssistant, Filler(5_000));
		}

		Assert.True(_files.GetFileStamp(f)!.Length > Constants.LastUsedSettingsTailBytes);
		Assert.Equal(new LastUsedSessionSettings("Early", "early-model", "low"), LastUsedSessionSettingsReader.Read(_files, f));
	}

	[Fact]
	public void LargeFile_WithValuesInTheTail_MatchesAFullParse()
	{
		var f = _files.CreateSessionFile();
		for (var i = 0; i < 200; i++)
		{
			_files.AppendMessage(f, Constants.SessionFile.RoleUser, Filler(2_000), profileName: "P" + i);
			_files.AppendMessage(f, Constants.SessionFile.RoleAssistant, Filler(2_000), modelId: "m" + i, effort: "e" + i);
		}

		var result = LastUsedSessionSettingsReader.Read(_files, f);

		Assert.Equal(new LastUsedSessionSettings("P199", "m199", "e199"), result);
		Assert.True(_files.GetFileStamp(f)!.Length > 4 * Constants.LastUsedSettingsTailBytes);
	}

	[Fact]
	public void OnlyOneKindPresent_StillReturnsIt()
	{
		var f = _files.CreateSessionFile();
		_files.AppendMessage(f, Constants.SessionFile.RoleUser, "hi", profileName: "OnlyProfile");

		Assert.Equal(new LastUsedSessionSettings("OnlyProfile", null, null), LastUsedSessionSettingsReader.Read(_files, f));
	}
}
