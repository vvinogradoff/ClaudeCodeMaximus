using System;
using System.IO;
using System.Linq;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Index build, append and rewrite detection, restart stability and sidecar recovery of the content store (P1.E).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionContentStoreIndexTests : ContentStoreTestBase
{
	private const string File1 = "a.txt";

	[Fact]
	public void MissingFile_HasNoHeaderAndNoEntries()
	{
		using var store = NewStore();

		Assert.False(store.GetHeader("nope.txt").Exists);
		Assert.Empty(store.ReadPage("nope.txt", new(null, null, 10)).Entries);
		Assert.Null(store.ReadEntry("nope.txt", 0));
	}

	[Fact]
	public void EmptyFile_HasAnEmptyHeader()
	{
		var file = Files.CreateSessionFile();
		using var store = NewStore();

		var header = store.GetHeader(file);

		Assert.True(header.Exists);
		Assert.Equal(0, header.EntryCount);
		Assert.Equal(0, header.NextOrdinal);
		Assert.Null(header.FirstTimestamp);
		Assert.Null(header.LastTimestamp);
	}

	[Fact]
	public void Build_IndexesEveryEntryAndFillsTheHeader()
	{
		WriteConversation(File1, 6);
		using var store = NewStore();

		var header = store.GetHeader(File1);

		Assert.True(header.Exists);
		Assert.Equal(6, header.EntryCount);
		Assert.Equal(6, header.NextOrdinal);
		Assert.Equal(Start, header.FirstTimestamp);
		Assert.Equal(Start.AddMinutes(5), header.LastTimestamp);
		Assert.Equal("work", header.LastProfileName);
		Assert.Equal("opus", header.LastModelId);
		Assert.Equal("high", header.LastEffort);
		Assert.NotEqual(Guid.Empty, header.FileId);
		Assert.Equal(new FileInfo(FullPath(File1)).Length, header.FileLength);
	}

	[Fact]
	public void Header_LastProfileModelEffort_AreTheLatestSeenAnywhere()
	{
		WriteRaw(File1,
			EntryText("USER", 0, "a", "profile=\"first\"") +
			EntryText("ASSISTANT", 1, "b", "model=\"m1\" effort=\"low\"") +
			EntryText("USER", 2, "c", "profile=\"second\"") +
			EntryText("ASSISTANT", 3, "d"));
		using var store = NewStore();

		var header = store.GetHeader(File1);

		Assert.Equal("second", header.LastProfileName);
		Assert.Equal("m1", header.LastModelId);
		Assert.Equal("low", header.LastEffort);
	}

	[Fact]
	public void Append_ThroughTheFileService_IndexesTheTailUnderTheSameFileId()
	{
		var file = Files.CreateSessionFile();
		Files.AppendMessage(file, Constants.SessionFile.RoleUser, "one");
		Files.AppendMessage(file, Constants.SessionFile.RoleAssistant, "two", modelId: "opus", effort: "high");
		using var store = NewStore();
		var before = store.GetHeader(file);

		Files.AppendMessage(file, Constants.SessionFile.RoleUser, "three", profileName: "work");
		var after = store.GetHeader(file);

		Assert.Equal(before.FileId, after.FileId);
		Assert.Equal(2, before.EntryCount);
		Assert.Equal(3, after.EntryCount);
		Assert.True(after.ContentVersion > before.ContentVersion);
		Assert.Equal("work", after.LastProfileName);
		Assert.Equal("three", store.ReadEntry(file, 2)!.Entry.Content);
	}

	[Fact]
	public void Append_WrittenBehindTheStoresBack_IsPickedUpOnNextRead()
	{
		WriteConversation(File1, 4);
		using var store = NewStore();
		var before = store.GetHeader(File1);

		AppendRaw(File1, EntryAt(4));
		AppendRaw(File1, EntryAt(5));
		var after = store.GetHeader(File1);

		Assert.Equal(before.FileId, after.FileId);
		Assert.Equal(6, after.EntryCount);
		Assert.Equal("m5", store.ReadEntry(File1, 5)!.Entry.Content);
	}

	[Fact]
	public void Append_ThatOnlyExtendsTheLastEntryBody_KeepsOrdinalsAndUpdatesTheEntry()
	{
		WriteConversation(File1, 3);
		using var store = NewStore();
		var before = store.GetHeader(File1);
		Assert.Equal("m2", store.ReadEntry(File1, 2)!.Entry.Content);

		AppendRaw(File1, "more text\n");
		var after = store.GetHeader(File1);

		Assert.Equal(before.FileId, after.FileId);
		Assert.Equal(3, after.EntryCount);
		Assert.Equal(Files.ReadEntries(File1)[2].Content, store.ReadEntry(File1, 2)!.Entry.Content);
		Assert.Contains("more text", store.ReadEntry(File1, 2)!.Entry.Content);
	}

	[Fact]
	public void Ordinals_OfExistingEntries_DoNotChangeWhenEntriesAreAppended()
	{
		WriteConversation(File1, 5);
		using var store = NewStore();
		var first = ReadAllFull(store, File1);

		AppendRaw(File1, EntryAt(5));
		var again = ReadAllFull(store, File1);

		Assert.Equal(6, again.Count);
		for (var i = 0; i < first.Count; i++)
		{
			Assert.Equal(first[i].Ordinal, again[i].Ordinal);
			Assert.Equal(first[i].ByteOffset, again[i].ByteOffset);
			Assert.Equal(first[i].ByteLength, again[i].ByteLength);
		}
	}

	[Fact]
	public void Rewrite_ThroughTheFileService_AssignsANewFileId()
	{
		WriteConversation(File1, 5);
		using var store = NewStore();
		var before = store.GetHeader(File1);

		Files.WriteSessionFile(File1, Files.ReadEntries(File1).Take(3).ToList());
		var after = store.GetHeader(File1);

		Assert.NotEqual(before.FileId, after.FileId);
		Assert.Equal(3, after.EntryCount);
	}

	[Fact]
	public void Rewrite_ToTheSameLengthWithOtherBytes_IsNotMistakenForAnAppend()
	{
		WriteConversation(File1, 5);
		using var store = NewStore();
		var before = store.GetHeader(File1);
		var original = File.ReadAllText(FullPath(File1));

		// Same length, same entry count, different first and last entries.
		var changed = original.Replace("m0", "x0").Replace("m4", "x4");
		Assert.Equal(original.Length, changed.Length);
		WriteRaw(File1, changed);
		var after = store.GetHeader(File1);

		Assert.NotEqual(before.FileId, after.FileId);
		Assert.Equal("x4", store.ReadEntry(File1, 4)!.Entry.Content);
	}

	[Fact]
	public void Rewrite_ToAShorterFile_IsRebuilt()
	{
		WriteConversation(File1, 8);
		using var store = NewStore();
		var before = store.GetHeader(File1);

		WriteConversation(File1, 3);
		var after = store.GetHeader(File1);

		Assert.NotEqual(before.FileId, after.FileId);
		Assert.Equal(3, after.EntryCount);
	}

	[Fact]
	public void Rewrite_ToALongerFileWithDifferentHistory_IsRebuilt()
	{
		WriteConversation(File1, 4);
		using var store = NewStore();
		var before = store.GetHeader(File1);

		WriteConversation(File1, 9, firstMinute: 100);
		var after = store.GetHeader(File1);

		Assert.NotEqual(before.FileId, after.FileId);
		Assert.Equal(9, after.EntryCount);
		Assert.Equal(Start.AddMinutes(100), after.FirstTimestamp);
	}

	[Fact]
	public void RangeRead_WithAnOldFileId_IsRejectedAfterARewrite()
	{
		WriteConversation(File1, 4);
		using var store = NewStore();
		var oldId = store.GetHeader(File1).FileId;
		store.ReadRange(File1, oldId, 0, 0, 100);

		Files.WriteSessionFile(File1, Files.ReadEntries(File1).Take(2).ToList());

		var ex = Assert.Throws<TdcpException>(() => store.ReadRange(File1, oldId, 0, 0, 100));
		Assert.Equal("Session history changed", ex.Message);
	}

	[Fact]
	public void Restart_KeepsFileIdAndOrdinals()
	{
		WriteConversation(File1, 6);
		Guid fileId;
		using (var first = NewStore())
		{
			fileId = first.GetHeader(File1).FileId;
			Assert.Single(SidecarFiles());
		}

		using var second = NewStore();
		var header = second.GetHeader(File1);

		Assert.Equal(fileId, header.FileId);
		Assert.Equal(6, header.EntryCount);
		Assert.Equal("m3", second.ReadEntry(File1, 3)!.Entry.Content);
	}

	[Fact]
	public void Restart_AfterAppendsTheOldStoreNeverFlushed_StillKeepsFileIdAndAssignsTheNextOrdinals()
	{
		WriteConversation(File1, 4);
		using var first = NewStore();
		var fileId = first.GetHeader(File1).FileId;

		// The sidecar is rewritten at most every few seconds, so after quick appends it is behind the file: a crash here
		// leaves a stale but valid sidecar.
		AppendRaw(File1, EntryAt(4));
		first.GetHeader(File1);
		AppendRaw(File1, EntryAt(5));
		first.GetHeader(File1);
		AppendRaw(File1, EntryAt(6));

		using var second = NewStore();
		var header = second.GetHeader(File1);

		Assert.Equal(fileId, header.FileId);
		Assert.Equal(7, header.EntryCount);
		Assert.Equal(7, header.NextOrdinal);
		Assert.Equal("m6", second.ReadEntry(File1, 6)!.Entry.Content);
	}

	[Fact]
	public void Restart_AfterTheFileWasRewrittenWhileTheDaemonWasDown_BuildsANewGeneration()
	{
		WriteConversation(File1, 6);
		Guid fileId;
		using (var first = NewStore())
			fileId = first.GetHeader(File1).FileId;

		WriteConversation(File1, 3, firstMinute: 50);
		using var second = NewStore();
		var header = second.GetHeader(File1);

		Assert.NotEqual(fileId, header.FileId);
		Assert.Equal(3, header.EntryCount);
	}

	[Fact]
	public void CorruptSidecar_IsRebuiltSilently()
	{
		WriteConversation(File1, 5);
		using (var first = NewStore())
			first.GetHeader(File1);

		var sidecar = Assert.Single(SidecarFiles());
		var bytes = File.ReadAllBytes(sidecar);
		bytes[bytes.Length / 2] ^= 0xFF;
		File.WriteAllBytes(sidecar, bytes);

		using var second = NewStore();
		var header = second.GetHeader(File1);

		Assert.Equal(5, header.EntryCount);
		Assert.Equal("m4", second.ReadEntry(File1, 4)!.Entry.Content);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(7)]
	public void TruncatedOrGarbageSidecar_IsRebuiltSilently(int keepBytes)
	{
		WriteConversation(File1, 5);
		using (var first = NewStore())
			first.GetHeader(File1);

		var sidecar = Assert.Single(SidecarFiles());
		File.WriteAllBytes(sidecar, File.ReadAllBytes(sidecar).Take(keepBytes).ToArray());

		using var second = NewStore();

		Assert.Equal(5, second.GetHeader(File1).EntryCount);
	}

	[Fact]
	public void MissingSidecar_IsRebuilt()
	{
		WriteConversation(File1, 5);
		using (var first = NewStore())
			first.GetHeader(File1);

		File.Delete(Assert.Single(SidecarFiles()));
		using var second = NewStore();

		Assert.Equal(5, second.GetHeader(File1).EntryCount);
	}

	[Fact]
	public void OrphanedSidecar_IsHarmlessAndRemovedOnceTheMissingFileIsSeen()
	{
		WriteConversation(File1, 3);
		using (var first = NewStore())
			first.GetHeader(File1);

		File.Delete(FullPath(File1));
		using var second = NewStore();

		Assert.False(second.GetHeader(File1).Exists);
		Assert.Empty(SidecarFiles()); // review B3: seeing the file missing clears the leftover sidecar
	}

	[Fact]
	public void SidecarFromAnotherFormatVersion_IsRebuilt()
	{
		WriteConversation(File1, 3);
		using (var first = NewStore())
			first.GetHeader(File1);

		var sidecar = Assert.Single(SidecarFiles());
		var bytes = File.ReadAllBytes(sidecar);
		BitConverter.GetBytes(Constants.ContentStore.FormatVersion + 1).CopyTo(bytes, sizeof(uint));
		File.WriteAllBytes(sidecar, bytes);

		using var second = NewStore();

		Assert.Equal(3, second.GetHeader(File1).EntryCount);
	}

	[Fact]
	public void Invalidate_RebuildsWithANewFileId()
	{
		WriteConversation(File1, 3);
		using var store = NewStore();
		var before = store.GetHeader(File1).FileId;

		store.Invalidate(File1);

		Assert.NotEqual(before, store.GetHeader(File1).FileId);
	}

	[Fact]
	public void DeletedFile_ReportsMissingAndARecreatedOneStartsAFreshGeneration()
	{
		WriteConversation(File1, 3);
		using var store = NewStore();
		var before = store.GetHeader(File1).FileId;

		File.Delete(FullPath(File1));
		Assert.False(store.GetHeader(File1).Exists);

		WriteConversation(File1, 2);
		var after = store.GetHeader(File1);

		Assert.NotEqual(before, after.FileId);
		Assert.Equal(2, after.EntryCount);
	}

	[Fact]
	public void ManyFiles_BeyondTheInMemoryLimit_StayCorrect()
	{
		using var store = NewStore();
		var names = Enumerable.Range(0, Constants.ContentStore.MaxIndexedFiles + 10).Select(i => $"f{i}.txt").ToList();
		foreach (var name in names)
			WriteConversation(name, 3);

		var ids = names.ToDictionary(n => n, n => store.GetHeader(n).FileId);

		foreach (var name in names)
		{
			var header = store.GetHeader(name);
			Assert.Equal(ids[name], header.FileId);
			Assert.Equal(3, header.EntryCount);
		}
	}
}
