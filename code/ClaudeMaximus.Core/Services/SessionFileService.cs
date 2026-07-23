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

	public void AppendMessage(string fileName, string role, string content, string? profileName = null, string? modelId = null, string? effort = null)
	{
		var entry = BuildEntryText(DateTimeOffset.UtcNow, role, content, profileName, modelId, effort);
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

	public void DeleteSessionFile(string fileName)
	{
		var fullPath = GetFullPath(fileName);
		if (File.Exists(fullPath))
			File.Delete(fullPath);
	}

	public void RewriteSessionFile(string fileName, string content)
	{
		var fullPath = GetFullPath(fileName);
		var tmpPath = fullPath + ".tmp";
		File.WriteAllText(tmpPath, content, Encoding.UTF8);
		File.Move(tmpPath, fullPath, overwrite: true);
	}

	public void WriteSessionFile(string fileName, IReadOnlyList<SessionEntryModel> entries)
	{
		var sb = new StringBuilder();
		foreach (var entry in entries)
		{
			if (entry.IsCompaction)
			{
				sb.AppendLine(FormatHeader(entry.Timestamp, Constants.SessionFile.RoleCompaction));
			}
			else
			{
				sb.AppendLine(FormatHeader(entry.Timestamp, entry.Role, entry.ProfileName, entry.ModelId, entry.Effort));
				sb.AppendLine(entry.Content);
				sb.AppendLine();
			}
		}

		var fullPath = GetFullPath(fileName);
		var tmpPath = fullPath + ".tmp";
		File.WriteAllText(tmpPath, sb.ToString(), Encoding.UTF8);
		File.Move(tmpPath, fullPath, overwrite: true);
	}

	public int RepairCorruptedCompactions()
	{
		var root = _appSettings.Settings.SessionFilesRoot;
		if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
			return 0;

		var repaired = 0;
		foreach (var filePath in Directory.GetFiles(root, $"*{Constants.SessionFileExtension}"))
		{
			try
			{
				var content = File.ReadAllText(filePath, Encoding.UTF8).Trim();
				if (string.IsNullOrEmpty(content))
					continue;

				var lines = File.ReadAllLines(filePath, Encoding.UTF8);
				var entries = ParseEntries(lines);

				// If the file has content but zero parseable entries, it's corrupted
				if (entries.Count > 0)
					continue;

				// Wrap the raw text with proper session headers
				var now = DateTimeOffset.UtcNow;
				var sb = new StringBuilder();
				sb.AppendLine(FormatHeader(now, Constants.SessionFile.RoleCompaction));
				sb.AppendLine(FormatHeader(now, Constants.SessionFile.RoleAssistant));
				sb.AppendLine(content);
				sb.AppendLine();

				var tmpPath = filePath + ".tmp";
				File.WriteAllText(tmpPath, sb.ToString(), Encoding.UTF8);
				File.Move(tmpPath, filePath, overwrite: true);
				repaired++;
			}
			catch
			{
				// Skip files we can't process
			}
		}

		return repaired;
	}

	public string GetFullPath(string fileName)
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

	private static string BuildEntryText(DateTimeOffset timestamp, string role, string content, string? profileName = null, string? modelId = null, string? effort = null)
	{
		var sb = new StringBuilder();
		sb.AppendLine(FormatHeader(timestamp, role, profileName, modelId, effort));
		sb.AppendLine(content);
		sb.AppendLine();
		return sb.ToString();
	}

	private static string FormatHeader(DateTimeOffset timestamp, string role, string? profileName = null, string? modelId = null, string? effort = null)
	{
		var sb = new StringBuilder($"[{timestamp.ToString(Constants.SessionFile.TimestampFormat)}] {role}");
		if (profileName != null)
			sb.Append($" profile=\"{EscapeMetaValue(profileName)}\"");
		if (modelId != null)
			sb.Append($" model=\"{EscapeMetaValue(modelId)}\"");
		if (effort != null)
			sb.Append($" effort=\"{EscapeMetaValue(effort)}\"");
		return sb.ToString();
	}

	private static string EscapeMetaValue(string value) => value.Replace("\"", "'");

	private static IReadOnlyList<SessionEntryModel> ParseEntries(string[] lines)
	{
		var entries = new List<SessionEntryModel>();
		var i = 0;

		while (i < lines.Length)
		{
			if (!TryParseHeader(lines[i], out var timestamp, out var role, out var rawMeta))
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

			var meta = ParseMetadata(rawMeta);
			meta.TryGetValue("profile", out var profileName);
			meta.TryGetValue("model", out var modelId);
			meta.TryGetValue("effort", out var effort);

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
				Timestamp   = timestamp,
				Role        = role,
				Content     = string.Join(Environment.NewLine, contentLines),
				ProfileName = profileName,
				ModelId     = modelId,
				Effort      = effort,
			});
		}

		return entries;
	}

	private static bool TryParseHeader(string line, out DateTimeOffset timestamp, out string role, out string rawMeta)
	{
		timestamp = default;
		role = string.Empty;
		rawMeta = string.Empty;

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

		// Everything after "] ": first word is the role, the rest is key="value" metadata
		var afterBracket = line[(closeBracket + 2)..];
		var spaceIdx = afterBracket.IndexOf(' ');
		if (spaceIdx < 0)
		{
			role = afterBracket.Trim();
		}
		else
		{
			role = afterBracket[..spaceIdx];
			rawMeta = afterBracket[(spaceIdx + 1)..];
		}

		return !string.IsNullOrEmpty(role);
	}

	/// <summary>Parses space-delimited key="value" pairs from a header metadata string.</summary>
	private static Dictionary<string, string> ParseMetadata(string rawMeta)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		if (string.IsNullOrWhiteSpace(rawMeta))
			return result;

		var i = 0;
		while (i < rawMeta.Length)
		{
			while (i < rawMeta.Length && char.IsWhiteSpace(rawMeta[i])) i++;
			var eqIndex = rawMeta.IndexOf('=', i);
			if (eqIndex < 0) break;
			var key = rawMeta[i..eqIndex].Trim();
			i = eqIndex + 1;
			if (i >= rawMeta.Length || rawMeta[i] != '"') break;
			i++;
			var closeQuote = rawMeta.IndexOf('"', i);
			if (closeQuote < 0) break;
			var value = rawMeta[i..closeQuote];
			i = closeQuote + 1;
			if (!string.IsNullOrEmpty(key))
				result[key] = value;
		}
		return result;
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
