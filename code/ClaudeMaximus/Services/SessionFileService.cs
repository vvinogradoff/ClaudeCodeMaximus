using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public sealed class SessionFileService : ISessionFileService
{
	private readonly IAppSettingsService _appSettings;

	public SessionFileService(IAppSettingsService appSettings)
	{
		_appSettings = appSettings;
	}

	public string CreateSessionFile()
	{
		var timestamp = DateTime.UtcNow.ToString(Constants.SessionFileNameDateFormat);
		var suffix = GenerateRandomSuffix();
		var fileName = $"{timestamp}-{suffix}{Constants.SessionFileExtension}";
		var fullPath = GetFullPath(fileName);
		File.WriteAllText(fullPath, string.Empty, Encoding.UTF8);
		return fileName;
	}

	public void AppendMessage(string fileName, string role, string content)
	{
		var entry = BuildEntryText(DateTimeOffset.UtcNow, role, content);
		AppendToFile(fileName, entry);
	}

	public void AppendCompactionSeparator(string fileName)
	{
		var header = FormatHeader(DateTimeOffset.UtcNow, Constants.SessionFile.RoleCompaction);
		AppendToFile(fileName, header + Environment.NewLine);
	}

	public IReadOnlyList<SessionEntryModel> ReadEntries(string fileName)
	{
		var fullPath = GetFullPath(fileName);
		if (!File.Exists(fullPath))
			return [];

		var lines = File.ReadAllLines(fullPath, Encoding.UTF8);
		return ParseEntries(lines);
	}

	public bool SessionFileExists(string fileName)
		=> File.Exists(GetFullPath(fileName));

	public void RewriteSessionFile(string fileName, string content)
	{
		var fullPath = GetFullPath(fileName);
		var tmpPath = fullPath + ".tmp";
		File.WriteAllText(tmpPath, content, Encoding.UTF8);
		File.Move(tmpPath, fullPath, overwrite: true);
	}

	private string GetFullPath(string fileName)
		=> Path.Combine(_appSettings.Settings.SessionFilesRoot, fileName);

	private void AppendToFile(string fileName, string text)
	{
		var fullPath = GetFullPath(fileName);
		using var stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read);
		using var writer = new StreamWriter(stream, Encoding.UTF8);
		writer.Write(text);
		writer.Flush();
		stream.Flush(flushToDisk: true);
	}

	private static string BuildEntryText(DateTimeOffset timestamp, string role, string content)
	{
		var sb = new StringBuilder();
		sb.AppendLine(FormatHeader(timestamp, role));
		sb.AppendLine(content);
		sb.AppendLine();
		return sb.ToString();
	}

	private static string FormatHeader(DateTimeOffset timestamp, string role)
		=> $"[{timestamp.ToString(Constants.SessionFile.TimestampFormat)}] {role}";

	private static IReadOnlyList<SessionEntryModel> ParseEntries(string[] lines)
	{
		var entries = new List<SessionEntryModel>();
		var i = 0;

		while (i < lines.Length)
		{
			if (!TryParseHeader(lines[i], out var timestamp, out var role))
			{
				i++;
				continue;
			}

			i++;

			if (role == Constants.SessionFile.RoleCompaction)
			{
				entries.Add(new SessionEntryModel
				{
					Timestamp = timestamp,
					Role = role,
					Content = string.Empty,
				});
				continue;
			}

			var contentLines = new List<string>();
			while (i < lines.Length && !IsHeaderLine(lines[i]))
			{
				contentLines.Add(lines[i]);
				i++;
			}

			// Trim trailing blank lines from content
			while (contentLines.Count > 0 && string.IsNullOrEmpty(contentLines[^1]))
				contentLines.RemoveAt(contentLines.Count - 1);

			entries.Add(new SessionEntryModel
			{
				Timestamp = timestamp,
				Role = role,
				Content = string.Join(Environment.NewLine, contentLines),
			});
		}

		return entries;
	}

	private static bool TryParseHeader(string line, out DateTimeOffset timestamp, out string role)
	{
		timestamp = default;
		role = string.Empty;

		if (!line.StartsWith('['))
			return false;

		var closeBracket = line.IndexOf(']');
		if (closeBracket < 0)
			return false;

		var timestampPart = line[1..closeBracket];
		if (!DateTimeOffset.TryParseExact(
				timestampPart,
				Constants.SessionFile.TimestampFormat,
				null,
				System.Globalization.DateTimeStyles.AssumeUniversal,
				out timestamp))
			return false;

		role = line[(closeBracket + 2)..].Trim();
		return !string.IsNullOrEmpty(role);
	}

	private static bool IsHeaderLine(string line)
		=> line.StartsWith('[') && line.Contains(']');

	private static string GenerateRandomSuffix()
	{
		const string chars = "abcdefghijklmnopqrstuvwxyz";
		var result = new char[Constants.SessionFileNameRandomSuffixLength];
		var rng = Random.Shared;
		for (var i = 0; i < result.Length; i++)
			result[i] = chars[rng.Next(chars.Length)];
		return new string(result);
	}
}
