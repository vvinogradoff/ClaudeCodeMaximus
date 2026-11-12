using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TessynDesktop.Models;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Builds the compaction prompt (FR.11.6): the transcript after the last COMPACTION marker, the project glossary
/// when present, and the template. Moved from the desktop's SessionPromptComposer so the desktop and the web share it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class CompactionPromptBuilder
{
	private static readonly ILogger _log = Log.ForContext(typeof(CompactionPromptBuilder));

	/// <summary>Builds the full prompt; null when there is nothing to compact yet.</summary>
	public static string? BuildPrompt(IReadOnlyList<SessionEntryModel> entries, string workingDirectory)
	{
		var transcript = BuildTranscript(entries);
		if (string.IsNullOrWhiteSpace(transcript))
			return null;

		var glossarySection = string.Empty;
		try
		{
			var glossaryPath = Path.Combine(workingDirectory, Constants.Compaction.GlossaryRelativeFolder, Constants.Compaction.GlossaryFileName);
			if (File.Exists(glossaryPath))
			{
				var glossaryContent = File.ReadAllText(glossaryPath);
				glossarySection = $"""

PROJECT GLOSSARY:
```
{glossaryContent}
```

""";
			}
		}
		catch (Exception ex)
		{
			_log.Debug("Could not read glossary for compaction: {Error}", ex.Message);
		}

		return string.Format(Constants.Instructions.CompactionPromptTemplate, glossarySection, transcript);
	}

	/// <summary>
	/// USER/ASSISTANT entries (non-empty) after the last COMPACTION entry, or all of them when there is none, each as
	/// "[timestamp] ROLE" followed by its content — the format Claude is asked to answer in. SYSTEM entries are excluded.
	/// </summary>
	public static string BuildTranscript(IReadOnlyList<SessionEntryModel> entries)
	{
		var lastCompactionIndex = -1;
		for (var i = 0; i < entries.Count; i++)
		{
			if (entries[i].Role == Constants.SessionFile.RoleCompaction)
				lastCompactionIndex = i;
		}

		var relevant = lastCompactionIndex >= 0 ? entries.Skip(lastCompactionIndex + 1) : entries;

		var sb = new StringBuilder();
		foreach (var entry in relevant)
		{
			if (entry.Role is not (Constants.SessionFile.RoleUser or Constants.SessionFile.RoleAssistant))
				continue;
			if (string.IsNullOrWhiteSpace(entry.Content))
				continue;

			sb.AppendLine($"[{entry.Timestamp.ToString(Constants.SessionFile.TimestampFormat)}] {entry.Role}");
			sb.AppendLine(entry.Content);
			sb.AppendLine();
		}

		return sb.ToString().TrimEnd();
	}
}
