using System;
using System.IO;
using System.Text;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class DraftService : IDraftService
{
	private readonly string _draftsDirectory;

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
		var path = GetDraftPath(sessionFileName);
		File.WriteAllText(path, text, Encoding.UTF8);
	}

	public void DeleteDraft(string sessionFileName)
	{
		var path = GetDraftPath(sessionFileName);
		if (File.Exists(path))
			File.Delete(path);
	}

	private string GetDraftPath(string sessionFileName)
		=> Path.Combine(_draftsDirectory, sessionFileName + Constants.DraftFileExtension);

	private static string GetDefaultDraftsDir()
	{
		var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		return Path.Combine(appData, Constants.AppDataFolderName, Constants.DraftsFolderName);
	}
}
