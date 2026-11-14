using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionContentStore" />
/// <remarks>Created by Claude</remarks>
public sealed class SessionContentStore : ISessionContentStore, IDisposable
{
	/// <summary>First byte of a UTF-8 continuation sequence is 10xxxxxx.</summary>
	private const byte ContinuationMask = 0xC0;
	private const byte ContinuationBits = 0x80;

	/// <summary>Bytes past a cut that show whether it falls inside a multi-byte character.</summary>
	private const int CharacterLookahead = 3;

	private readonly ISessionFileService _files;
	private readonly EntryIndexCatalog _catalog;

	public SessionContentStore(ISessionFileService files) : this(files, GetDefaultDirectory())
	{
	}

	/// <summary>Constructor for testing: allows injecting a custom sidecar directory.</summary>
	public SessionContentStore(ISessionFileService files, string indexDirectory)
	{
		_files   = files;
		_catalog = new EntryIndexCatalog(files, indexDirectory);
	}

	public void Dispose() => _catalog.Dispose();

	private static string GetDefaultDirectory()
	{
		var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		return Path.Combine(appData, Constants.AppDataFolderName, Constants.ContentStore.FolderName);
	}

	/// <inheritdoc />
	public SessionContentHeaderModel GetHeader(string fileName)
	{
		var snapshot = _catalog.Acquire(fileName);
		return snapshot?.ToHeader() ?? new SessionContentHeaderModel(false, Guid.Empty, 0, 0, null, null, null, null, null, 0, 0);
	}

	/// <inheritdoc />
	public SessionContentLastUsedModel GetLastUsed(string fileName)
	{
		for (var attempt = 0; attempt <= Constants.ContentStore.MaxReadRetries; attempt++)
		{
			var snapshot = _catalog.Acquire(fileName, rebuild: attempt > 0);
			if (snapshot == null || snapshot.Count == 0)
				return new SessionContentLastUsedModel(null, null, null);

			var profileAt = LastIndexWhere(snapshot, EntryRoleCode.User, EntryIndexFlags.HasProfile);
			var modelAt   = LastIndexWhere(snapshot, EntryRoleCode.Assistant, EntryIndexFlags.HasModel);

			// Only the header line matters, so the entries are read cut to the header prefix.
			var profileEntry = profileAt < 0 ? null : ReadEntries(fileName, snapshot, profileAt, profileAt + 1, Constants.ContentStore.HeaderPrefixMaxBytes);
			var modelEntry   = modelAt < 0 ? null : ReadEntries(fileName, snapshot, modelAt, modelAt + 1, Constants.ContentStore.HeaderPrefixMaxBytes);
			if ((profileAt >= 0 && profileEntry == null) || (modelAt >= 0 && modelEntry == null))
				continue;

			return new SessionContentLastUsedModel(
				profileEntry?[0].Entry.ProfileName,
				modelEntry?[0].Entry.ModelId,
				modelEntry?[0].Entry.Effort);
		}

		throw new TdcpException(ChangedMessage);
	}

	/// <summary>The position of the last entry of <paramref name="role"/> whose header carries <paramref name="flag"/>; -1 when none.</summary>
	private static int LastIndexWhere(EntryIndexSnapshot snapshot, EntryRoleCode role, EntryIndexFlags flag)
	{
		for (var position = snapshot.Count - 1; position >= 0; position--)
		{
			var record = snapshot.Records[position];
			if (record.Role == role && (record.Flags & flag) != 0)
				return position;
		}

		return -1;
	}

	/// <inheritdoc />
	public SessionContentPageModel ReadPage(string fileName, SessionContentPageRequest request)
	{
		for (var attempt = 0; attempt <= Constants.ContentStore.MaxReadRetries; attempt++)
		{
			var snapshot = _catalog.Acquire(fileName, rebuild: attempt > 0);
			if (snapshot == null)
				return new SessionContentPageModel(Guid.Empty, 0, [], false, false, 0);

			var (first, end) = SelectPage(snapshot, request);
			var entryMax     = Math.Max(request.EntryMaxBytes, Constants.ContentStore.HeaderPrefixMaxBytes);

			var entries = ReadEntries(fileName, snapshot, first, end, entryMax);
			if (entries != null)
				return new SessionContentPageModel(snapshot.FileId, snapshot.Count, entries, first > 0, end < snapshot.Count, snapshot.ContentVersion);
		}

		throw new TdcpException(ChangedMessage);
	}

	/// <inheritdoc />
	public SessionContentEntryModel? ReadEntry(string fileName, int ordinal)
	{
		for (var attempt = 0; attempt <= Constants.ContentStore.MaxReadRetries; attempt++)
		{
			var snapshot = _catalog.Acquire(fileName, rebuild: attempt > 0);
			if (snapshot == null || ordinal < 0 || ordinal >= snapshot.Count)
				return null;

			var entries = ReadEntries(fileName, snapshot, ordinal, ordinal + 1, int.MaxValue);
			if (entries != null)
				return entries[0];
		}

		throw new TdcpException(ChangedMessage);
	}

	/// <inheritdoc />
	public SessionContentRangeModel ReadRange(string fileName, Guid fileId, int ordinal, long startOffset, int maxBytes)
	{
		var snapshot = _catalog.Acquire(fileName);
		if (snapshot == null || snapshot.FileId != fileId)
			throw new TdcpException(ChangedMessage);

		if (ordinal < 0 || ordinal >= snapshot.Count)
			throw new TdcpException("Entry not found");

		var record = snapshot.Records[ordinal];
		var start  = Math.Clamp(startOffset, 0, record.Length);
		var want   = Math.Max(maxBytes <= 0 ? Constants.ContentStore.DefaultRangeMaxBytes : maxBytes, CharacterLookahead + 1);
		var read   = (int)Math.Min(record.Length - start, (long)want + CharacterLookahead);

		var buffer = new byte[read];
		if (!TryRead(fileName, snapshot, record.Offset + start, buffer))
			throw new TdcpException(ChangedMessage);

		// Both ends are moved onto character boundaries: a start inside a character skips its tail, an end inside one stops before it.
		var from = 0;
		while (from < read && start + from < record.Length && (buffer[from] & ContinuationMask) == ContinuationBits)
			from++;

		var to = (int)Math.Min(read, from + (long)want);
		if (start + to < record.Length)
			to = BackOffContinuation(buffer, from, to);

		var text = Encoding.UTF8.GetString(buffer, from, to - from);
		return new SessionContentRangeModel(snapshot.FileId, ordinal, start + from, start + to, record.Length, text);
	}

	/// <inheritdoc />
	public void Invalidate(string fileName) => _catalog.Invalidate(fileName);

	/// <inheritdoc />
	public void Flush() => _catalog.Flush();

	private const string ChangedMessage = "Session history changed";

	/// <summary>The positions [first, end) a page covers, within the budget of entries and bytes.</summary>
	private static (int First, int End) SelectPage(EntryIndexSnapshot snapshot, SessionContentPageRequest request)
	{
		var total = snapshot.Count;
		if (request.MaxEntries <= 0 || total == 0)
			return (0, 0);

		var entryMax = Math.Max(request.EntryMaxBytes, Constants.ContentStore.HeaderPrefixMaxBytes);
		long Cost(int position) => Math.Min(snapshot.Records[position].Length, entryMax);

		var bytes = 0L;
		if (request.AfterOrdinal.HasValue)
		{
			var first = Math.Clamp(request.AfterOrdinal.Value + 1L, 0, total);
			var end   = (int)first;
			while (end < total && end - first < request.MaxEntries && (end == first || bytes + Cost(end) <= request.MaxBytes))
				bytes += Cost(end++);

			return ((int)first, end);
		}

		var last  = request.BeforeOrdinal.HasValue ? (int)Math.Clamp(request.BeforeOrdinal.Value, 0, total) : total;
		var start = last;
		while (start > 0 && last - start < request.MaxEntries && (start == last || bytes + Cost(start - 1) <= request.MaxBytes))
			bytes += Cost(--start);

		return (start, last);
	}

	/// <summary>
	/// Reads and parses the entries at positions [first, end); null when what is on disk disagrees with the index (the
	/// file was rewritten or shortened after the snapshot), in which case the caller re-validates and tries again.
	/// </summary>
	private List<SessionContentEntryModel>? ReadEntries(string fileName, EntryIndexSnapshot snapshot, int first, int end, int entryMax)
	{
		var entries = new List<SessionContentEntryModel>(Math.Max(end - first, 0));
		if (end <= first)
			return entries;

		using var handle = OpenHandle(fileName);
		if (handle == null || RandomAccess.GetLength(handle) < snapshot.IndexedLength)
			return null;

		for (var position = first; position < end; position++)
		{
			var record = snapshot.Records[position];
			var want   = (int)Math.Min(record.Length, (long)entryMax + CharacterLookahead);
			var buffer = new byte[want];
			if (!ReadFully(handle, record.Offset, buffer))
				return null;

			var shown = record.Length;
			if (record.Length > entryMax)
				shown = BackOffContinuation(buffer, 0, entryMax);

			var parsed = Parse(buffer, shown);
			if (parsed == null || parsed.Timestamp.UtcTicks != record.TimestampTicks)
				return null;

			entries.Add(new SessionContentEntryModel(record.Ordinal, record.Offset, record.Length, parsed, shown < record.Length, shown));
		}

		return entries;
	}

	/// <summary>Runs the session-file parser over the entry's bytes; null unless they hold exactly one entry.</summary>
	private static SessionEntryModel? Parse(byte[] buffer, int length)
	{
		var lines = new List<string>();
		using (var reader = new StringReader(Encoding.UTF8.GetString(buffer, 0, length)))
		{
			while (reader.ReadLine() is { } line)
				lines.Add(line);
		}

		var parsed = SessionFileService.ParseEntries(lines.ToArray());
		return parsed.Count == 1 ? parsed[0] : null;
	}

	/// <summary>Moves <paramref name="cut"/> back so the bytes before it end on a character boundary.</summary>
	private static int BackOffContinuation(byte[] buffer, int floor, int cut)
	{
		while (cut > floor && cut < buffer.Length && (buffer[cut] & ContinuationMask) == ContinuationBits)
			cut--;

		return cut;
	}

	private bool TryRead(string fileName, EntryIndexSnapshot snapshot, long offset, byte[] buffer)
	{
		using var handle = OpenHandle(fileName);
		return handle != null && RandomAccess.GetLength(handle) >= snapshot.IndexedLength && ReadFully(handle, offset, buffer);
	}

	private Microsoft.Win32.SafeHandles.SafeFileHandle? OpenHandle(string fileName)
	{
		try
		{
			return File.OpenHandle(_files.GetFullPath(fileName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		}
		catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
		{
			return null;
		}
	}

	private static bool ReadFully(Microsoft.Win32.SafeHandles.SafeFileHandle handle, long offset, byte[] buffer)
	{
		var done = 0;
		while (done < buffer.Length)
		{
			var read = RandomAccess.Read(handle, buffer.AsSpan(done), offset + done);
			if (read == 0)
				return false;

			done += read;
		}

		return true;
	}
}
