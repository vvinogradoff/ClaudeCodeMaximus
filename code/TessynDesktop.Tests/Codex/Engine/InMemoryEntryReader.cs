using System;
using System.Collections.Generic;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services.Turns;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>Entry ids over the in-memory transcript: one file generation, an entry's ordinal is its index (as the content store's).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class InMemoryEntryReader(InMemorySessionFileService files) : ISessionEntryReader
{
	public Guid FileId { get; } = Guid.NewGuid();

	public SessionContentHeaderModel? GetHeader(string fileName)
	{
		var count = files.Entries.Count;
		return new SessionContentHeaderModel(true, FileId, count, count, null, null, null, null, null, count, 0);
	}

	public SessionEntryDto GetEntryById(string fileName, Guid fileId, int ordinal)
	{
		var entry = files.Entries[ordinal];
		return new SessionEntryDto { Index = ordinal, Timestamp = entry.Timestamp, Role = entry.Role, Content = entry.Content };
	}

	public IReadOnlyList<SessionEntryModel> GetAll(string fileName) => files.Entries;

	public SessionEntriesResult GetPage(string nodeId, string fileName, int? beforeIndex, int? afterIndex, int limit) => throw new NotSupportedException();

	public SessionEntriesResult GetPage(string nodeId, string fileName, int? beforeIndex, int? afterIndex, int limit, Guid expectedFileId) =>
		throw new NotSupportedException();

	public SessionContentLastUsedModel? GetLastUsed(string fileName) => null;

	public SessionEntryDto GetEntry(string fileName, int index, int epoch) => throw new NotSupportedException();
}
