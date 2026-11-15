using System;
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

	public string? LoadDraft(string sessionFileName)
	{
		var path = GetDraftPath(sessionFileName);
		if (!File.Exists(path))
			return null;

		var text = File.ReadAllText(path, Encoding.UTF8);
		return string.IsNullOrEmpty(text) ? null : text;
	}

	public void SaveDraft(string sessionFileName, string text)
	{
		bool changed;
		lock (_writeGate)
			changed = Write(sessionFileName, text);

		if (changed)
			DraftChanged?.Invoke(this, new DraftChangedEventArgs(sessionFileName));
	}

	public void DeleteDraft(string sessionFileName)
	{
		bool changed;
		lock (_writeGate)
			changed = Delete(sessionFileName);

		if (changed)
			DraftChanged?.Invoke(this, new DraftChangedEventArgs(sessionFileName));
	}

	public bool TryWriteDraft(string sessionFileName, string text, Func<string?, bool> isExpected)
	{
		bool changed;
		lock (_writeGate)
		{
			if (!isExpected(LoadDraft(sessionFileName)))
				return false;

			changed = text.Length == 0 ? Delete(sessionFileName) : Write(sessionFileName, text);
		}

		if (changed)
			DraftChanged?.Invoke(this, new DraftChangedEventArgs(sessionFileName));
		return true;
	}

	private bool Write(string sessionFileName, string text)
	{
		File.WriteAllText(GetDraftPath(sessionFileName), text, Encoding.UTF8);
		return true;
	}

	private bool Delete(string sessionFileName)
	{
		var path = GetDraftPath(sessionFileName);
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

	private static string GetDefaultDraftsDir()
	{
		var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		return Path.Combine(appData, Constants.AppDataFolderName, Constants.DraftsFolderName);
	}
}
