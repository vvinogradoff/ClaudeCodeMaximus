using System;
using System.IO;
using System.Linq;
using System.Text;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Serilog;

namespace TessynDesktop.ViewModels;

/// <summary>
/// Builds the hidden instruction block and context-reload preamble sent
/// alongside user messages (FR.11.9, FR.11.10); the compaction prompt moved to Core's CompactionPromptBuilder. Extracted from SessionViewModel (Stage 7).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionPromptComposer
{
	private readonly ISessionFileService _fileService;
	private readonly IAppSettingsService _appSettings;
	private readonly SessionNodeViewModel _node;
	private readonly IDaemonClient _daemonClient;

	public SessionPromptComposer(ISessionFileService fileService, IAppSettingsService appSettings, SessionNodeViewModel node, IDaemonClient daemonClient)
	{
		_fileService  = fileService;
		_appSettings  = appSettings;
		_node         = node;
		_daemonClient = daemonClient;
	}

	/// <summary>
	/// Builds the hidden instruction block appended to the user's message for claude stdin
	/// (FR.11.9), via the shared <see cref="InstructionBlockBuilder"/> so interactive and headless
	/// (<c>SessionTurnService</c>) turns emit identical instructions. <paramref name="isNewSession"/>
	/// should be true only when this turn is about to start a fresh claude process (no
	/// ClaudeSessionId/ExternalId yet) — the tool-usage bans are gated on it so they are sent once
	/// per fresh process instead of on every turn. <paramref name="isStreamingMode"/> reflects the
	/// mode of the spawn this turn is actually going to (FR.19.9), gating the native-scheduling
	/// redirect specifically.
	/// </summary>
	public string BuildInstructionBlock(bool? isAutoCommit, bool isNewBranch, bool isAutoDocument, bool isNewSession, bool isStreamingMode)
	{
		// FR.23.11 — pinned items and the user's Focus-view closures, via the daemon boundary. Taking the
		// closures clears their flag, so it is done only when the block will actually report them.
		var isWorker       = _node.Model.OrchestrationDepth > 0;
		var hasAttention   = _appSettings.Settings.AgentToolsEnabled && !isWorker;
		var nodeId         = _node.Model.NodeId;

		// FR.24.6 — same gate as the attention lines; taking it consumes the once-per-run flag, so only when it can be shown.
		var tagGuidanceDue = hasAttention && _daemonClient.TakeTagGuidanceDue(nodeId);

		var options = new InstructionOptionsModel(
			isAutoCommit, isNewBranch, isAutoDocument,
			_appSettings.Settings.AgentToolsEnabled, isNewSession, isStreamingMode,
			IsTelegramEnabled: _appSettings.Settings.TelegramEnabled,
			IsWorkerSession: isWorker,
			PinnedAttentionItems: hasAttention ? _daemonClient.GetAttentionItems(nodeId) : null,
			UserClosedAttentionItems: hasAttention ? _daemonClient.TakeUnreportedAttentionClosures(nodeId) : null,
			IsTagGuidanceDue: tagGuidanceDue,
			SessionTags: tagGuidanceDue ? _daemonClient.GetSessionTags(nodeId) : null);
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

	/// <summary>Builds the [model, effort] label for the current command bar selection.</summary>
	public static string BuildModelLabel(string? modelId, string? effort)
	{
		return $"[{modelId ?? "default"}, {effort ?? "default"}]";
	}
}
