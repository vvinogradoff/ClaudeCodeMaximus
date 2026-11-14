using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>A real session file service over a temp directory, with helpers to write session files the way the app does.</summary>
/// <remarks>Created by Claude</remarks>
public abstract class ContentStoreTestBase : IDisposable
{
	protected static readonly DateTimeOffset Start = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);

	protected readonly string Root;
	protected readonly string IndexDirectory;
	protected readonly SessionFileService Files;

	protected ContentStoreTestBase()
	{
		Root = Path.Combine(Path.GetTempPath(), "cm_test_store_" + Path.GetRandomFileName());
		Directory.CreateDirectory(Root);
		IndexDirectory = Path.Combine(Root, "entry-index");

		var settings = new AppSettingsService(Root);
		settings.Settings.SessionFilesRoot = Root;
		Files = new SessionFileService(settings);
	}

	public virtual void Dispose()
	{
		try
		{
			Directory.Delete(Root, recursive: true);
		}
		catch (IOException)
		{
		}
	}

	protected SessionContentStore NewStore() => new(Files, IndexDirectory);

	protected string FullPath(string fileName) => Files.GetFullPath(fileName);

	/// <summary>The text of one entry in the on-disk format.</summary>
	protected static string EntryText(string role, int minute, string body, string? meta = null) =>
		$"[{Start.AddMinutes(minute).ToString(Constants.SessionFile.TimestampFormat)}] {role}{(meta == null ? "" : " " + meta)}\n{body}\n\n";

	/// <summary>Writes the session file as raw text (UTF-8 with the byte order mark the app's files carry).</summary>
	protected string WriteRaw(string fileName, string text, bool bom = true)
	{
		File.WriteAllText(FullPath(fileName), text, new UTF8Encoding(bom));
		return fileName;
	}

	protected void AppendRaw(string fileName, string text) => File.AppendAllText(FullPath(fileName), text, new UTF8Encoding(false));

	/// <summary>A file of <paramref name="count"/> alternating user/assistant entries whose bodies are <c>m{i}</c>.</summary>
	protected string WriteConversation(string fileName, int count, int firstMinute = 0)
	{
		var sb = new StringBuilder();
		for (var i = 0; i < count; i++)
			sb.Append(EntryAt(i, firstMinute));

		return WriteRaw(fileName, sb.ToString());
	}

	protected static string EntryAt(int i, int firstMinute = 0) => i % 2 == 0
		? EntryText("USER", firstMinute + i, $"m{i}", "profile=\"work\"")
		: EntryText("ASSISTANT", firstMinute + i, $"m{i}", "model=\"opus\" effort=\"high\"");

	/// <summary>The same slice of a full parse as <paramref name="page"/> claims to hold, compared field by field.</summary>
	protected static void AssertSameEntries(IReadOnlyList<SessionEntryModel> expected, IReadOnlyList<SessionContentEntryModel> actual, int firstOrdinal)
	{
		Xunit.Assert.Equal(expected.Count, actual.Count);
		for (var i = 0; i < expected.Count; i++)
		{
			Xunit.Assert.Equal(firstOrdinal + i, actual[i].Ordinal);
			AssertSameEntry(expected[i], actual[i].Entry);
		}
	}

	protected static void AssertSameEntry(SessionEntryModel expected, SessionEntryModel actual)
	{
		Xunit.Assert.Equal(expected.Timestamp, actual.Timestamp);
		Xunit.Assert.Equal(expected.Role, actual.Role);
		Xunit.Assert.Equal(expected.Content, actual.Content);
		Xunit.Assert.Equal(expected.ProfileName, actual.ProfileName);
		Xunit.Assert.Equal(expected.ModelId, actual.ModelId);
		Xunit.Assert.Equal(expected.Effort, actual.Effort);
	}

	protected IReadOnlyList<SessionContentEntryModel> ReadAllFull(ISessionContentStore store, string fileName)
	{
		var total = store.GetHeader(fileName).EntryCount;
		return store.ReadPage(fileName, new SessionContentPageRequest(null, null, Math.Max(total, 1), int.MaxValue, int.MaxValue)).Entries;
	}

	protected string[] SidecarFiles() => Directory.Exists(IndexDirectory) ? Directory.GetFiles(IndexDirectory) : [];
}
