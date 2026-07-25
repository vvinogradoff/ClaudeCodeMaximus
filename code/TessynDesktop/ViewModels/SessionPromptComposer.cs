using System;
using System.IO;
using System.Linq;
using System.Text;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Serilog;

namespace TessynDesktop.ViewModels;

/// <summary>
/// Builds the hidden instruction block, context-reload preamble, and compaction prompt sent
/// alongside user messages (FR.11.9, FR.11.10, FR.11.6). Extracted from SessionViewModel (Stage 7).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionPromptComposer
{
	private static readonly ILogger _log = Log.ForContext<SessionPromptComposer>();

	private readonly ISessionFileService _fileService;
	private readonly IAppSettingsService _appSettings;
	private readonly SessionNodeViewModel _node;

	public SessionPromptComposer(ISessionFileService fileService, IAppSettingsService appSettings, SessionNodeViewModel node)
	{
		_fileService = fileService;
		_appSettings = appSettings;
		_node        = node;
	}

	/// <summary>
	/// Builds the hidden instruction block appended to the user's message for claude stdin
	/// (FR.11.9), via the shared <see cref="InstructionBlockBuilder"/> so interactive and headless
	/// (<c>SessionTurnService</c>) turns emit identical instructions. <paramref name="isNewSession"/>
	/// should be true only when this turn is about to start a fresh claude process (no
	/// ClaudeSessionId/ExternalId yet) — the tool-usage bans are gated on it so they are sent once
	/// per fresh process instead of on every turn.
	/// </summary>
	public string BuildInstructionBlock(bool? isAutoCommit, bool isNewBranch, bool isAutoDocument, bool isNewSession)
	{
		var options = new InstructionOptionsModel(
			isAutoCommit, isNewBranch, isAutoDocument,
			_appSettings.Settings.AgentToolsEnabled, isNewSession);
		return InstructionBlockBuilder.Build(options);
	}

	public string BuildContextPreamble(string currentMessage)
	{
		var entries = _fileService.ReadEntries(_node.FileName);
		var conversationEntries = entries
			.Where(e => e.Role is Constants.SessionFile.RoleUser or Constants.SessionFile.RoleAssistant)
			.ToList();

		if (conversationEntries.Count == 0)
			return currentMessage;

		var sb = new StringBuilder();
		sb.AppendLine("The following is the conversation history from a previous session that is no longer available. Use it as context for continuity:");
		sb.AppendLine("---");

		foreach (var entry in conversationEntries)
		{
			var roleLabel = entry.Role == Constants.SessionFile.RoleUser ? "Human" : "Assistant";
			sb.AppendLine($"[{roleLabel}]: {entry.Content}");
			sb.AppendLine();
		}

		sb.AppendLine("---");
		sb.AppendLine("Now, continuing the conversation:");
		sb.AppendLine(currentMessage);

		return sb.ToString();
	}

	/// <summary>Builds the full compaction prompt, injecting glossary content if available.</summary>
	public string BuildCompactionPrompt()
	{
		var glossarySection = string.Empty;
		try
		{
			var glossaryPath = Path.Combine(_node.Model.WorkingDirectory, "docs", "glossary.md");
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

		return string.Format(Constants.Instructions.CompactionPromptTemplate, glossarySection);
	}

	/// <summary>Builds the [model, effort] label for the current command bar selection.</summary>
	public static string BuildModelLabel(string? modelId, string? effort)
	{
		return $"[{modelId ?? "default"}, {effort ?? "default"}]";
	}
}
