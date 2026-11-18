using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class DraftService : IDraftService
{
	private readonly string _draftsDirectory;

	// Every write takes it, so a compare-and-write (TryWriteDraft) cannot lose a save of the desktop composer made in between.
	private readonly object _writeGate = new();

	/// <inheritdoc />
	public event EventHandler<DraftChangedEventArgs>? DraftChanged;

	public DraftService() : this(GetDefaultDraftsDir())
	{
	}

	/// <summary>Constructor for testing — allows injecting a custom base directory.</summary>
	public DraftService(string draftsDirectory)
	{
		_draftsDirectory = draftsDirectory;
		Directory.CreateDirectory(_draftsDirectory);
	}

	public string? LoadDraft(string sessionFileName) => Read(GetDraftPath(sessionFileName));

	public void SaveDraft(string sessionFileName, string text) => SaveCore(sessionFileName, null, text);

	public void DeleteDraft(string sessionFileName) => DeleteCore(sessionFileName, null);

	public bool TryWriteDraft(string sessionFileName, string text, Func<string?, bool> isExpected)
		=> TryWriteCore(sessionFileName, null, text, isExpected);

	public string? LoadItemDraft(string sessionFileName, string itemId) => Read(GetItemDraftPath(sessionFileName, itemId));

	public void SaveItemDraft(string sessionFileName, string itemId, string text) => SaveCore(sessionFileName, itemId, text);

	public void DeleteItemDraft(string sessionFileName, string itemId) => DeleteCore(sessionFileName, itemId);

	public bool TryWriteItemDraft(string sessionFileName, string itemId, string text, Func<string?, bool> isExpected)
		=> TryWriteCore(sessionFileName, itemId, text, isExpected);

	public IReadOnlyList<string> ListItemDraftIds(string sessionFileName)
	{
		var prefix = sessionFileName + Constants.ItemDraftInfix;
		var ids = new List<string>();
		foreach (var path in Directory.EnumerateFiles(_draftsDirectory, prefix + "*" + Constants.DraftFileExtension))
		{
			var name = Path.GetFileName(path);
			var id = name[prefix.Length..^Constants.DraftFileExtension.Length];
			if (IsValidItemId(id))
				ids.Add(id);
		}
		return ids;
	}

	public IReadOnlyList<ItemDraftStampModel> ListItemDraftStamps(string sessionFileName)
	{
		var prefix = sessionFileName + Constants.ItemDraftInfix;
		var stamps = new List<ItemDraftStampModel>();
		// FileInfo from the enumeration carries length and write time from the directory listing: no extra stat call per file.
		foreach (var info in new DirectoryInfo(_draftsDirectory).EnumerateFiles(prefix + "*" + Constants.DraftFileExtension))
		{
			var id = info.Name[prefix.Length..^Constants.DraftFileExtension.Length];
			if (IsValidItemId(id))
				stamps.Add(new ItemDraftStampModel(id, new SessionFileStampModel(info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero))));
		}
		return stamps;
	}

	public DateTimeOffset? GetItemDraftTimestamp(string sessionFileName, string itemId)
	{
		var path = GetItemDraftPath(sessionFileName, itemId);
		return File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) : null;
	}

	public void DeleteItemDrafts(string sessionFileName)
	{
		foreach (var id in ListItemDraftIds(sessionFileName))
			DeleteItemDraft(sessionFileName, id);
	}

	/// <summary>True for an item id that can be part of a draft file name: letters, digits, '_' and '-', at most <see cref="Constants.MaxItemIdLength"/> characters.</summary>
	public static bool IsValidItemId(string? itemId)
	{
		if (string.IsNullOrEmpty(itemId) || itemId.Length > Constants.MaxItemIdLength)
			return false;

		foreach (var c in itemId)
		{
			if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-'))
				return false;
		}
		return true;
	}

	private void SaveCore(string sessionFileName, string? itemId, string text)
	{
		bool changed;
		lock (_writeGate)
			changed = Write(PathOf(sessionFileName, itemId), text);

		if (changed)
			DraftChanged?.Invoke(this, new DraftChangedEventArgs(sessionFileName, itemId));
	}

	private void DeleteCore(string sessionFileName, string? itemId)
	{
		bool changed;
		lock (_writeGate)
			changed = Delete(PathOf(sessionFileName, itemId));

		if (changed)
			DraftChanged?.Invoke(this, new DraftChangedEventArgs(sessionFileName, itemId));
	}

	private bool TryWriteCore(string sessionFileName, string? itemId, string text, Func<string?, bool> isExpected)
	{
		var path = PathOf(sessionFileName, itemId);
		bool changed;
		lock (_writeGate)
		{
			if (!isExpected(Read(path)))
				return false;

			changed = text.Length == 0 ? Delete(path) : Write(path, text);
		}

		if (changed)
			DraftChanged?.Invoke(this, new DraftChangedEventArgs(sessionFileName, itemId));
		return true;
	}

	private string PathOf(string sessionFileName, string? itemId)
		=> itemId == null ? GetDraftPath(sessionFileName) : GetItemDraftPath(sessionFileName, itemId);

	// A read takes the same gate as the writes: a read that holds the file open made a write throw (sharing violation), and a read that met a write half done saw
	// a torn file. Reads are rare and short, so the gate costs nothing.
	private string? Read(string path)
	{
		lock (_writeGate)
		{
			if (!File.Exists(path))
				return null;

			var text = File.ReadAllText(path, Encoding.UTF8);
			return string.IsNullOrEmpty(text) ? null : text;
		}
	}

	private static bool Write(string path, string text)
	{
		File.WriteAllText(path, text, Encoding.UTF8);
		return true;
	}

	private static bool Delete(string path)
	{
		if (!File.Exists(path))
			return false;

		File.Delete(path);
		return true;
	}

	public DateTimeOffset? GetDraftTimestamp(string sessionFileName)
	{
		var path = GetDraftPath(sessionFileName);
		return File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) : null;
	}

	public SessionFileStampModel? GetDraftStamp(string sessionFileName)
	{
		var info = new FileInfo(GetDraftPath(sessionFileName));
		return info.Exists ? new SessionFileStampModel(info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)) : null;
	}

	private string GetDraftPath(string sessionFileName)
		=> Path.Combine(_draftsDirectory, sessionFileName + Constants.DraftFileExtension);

	private string GetItemDraftPath(string sessionFileName, string itemId)
	{
		if (!IsValidItemId(itemId))
			throw new ArgumentException("Not a valid Focus item id", nameof(itemId));

		return Path.Combine(_draftsDirectory, sessionFileName + Constants.ItemDraftInfix + itemId + Constants.DraftFileExtension);
	}

	private static string GetDefaultDraftsDir()
	{
		return Path.Combine(Constants.DataRoot, Constants.DraftsFolderName);
	}
}
