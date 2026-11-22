using System;
using System.Text.RegularExpressions;
using TessynDesktop.Codex.Events;
using TessynDesktop.Codex.Protocol;

namespace TessynDesktop.Codex;

/// <summary>
/// The one-line <c>turn.tool</c> summaries of FR.33.11: <c>$ &lt;command&gt;</c> (with the exit code when it fails),
/// <c>edit &lt;n&gt; file(s): &lt;first path&gt;</c>, <c>&lt;server&gt;.&lt;tool&gt;</c>, <c>search "&lt;query&gt;"</c>, <c>view &lt;path&gt;</c>.
/// Codex runs commands through <c>powershell.exe -Command '…'</c>; the summary shows only the inner command.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class CodexToolSummarizer
{
	/// <summary>The tool kind of an item type, or null for an item that is not a tool call.</summary>
	public CodexToolKind? KindOf(string itemType) => itemType switch
	{
		Constants.Codex.Protocol.ItemCommandExecution => CodexToolKind.Command,
		Constants.Codex.Protocol.ItemFileChange       => CodexToolKind.FileChange,
		Constants.Codex.Protocol.ItemMcpToolCall      => CodexToolKind.McpTool,
		Constants.Codex.Protocol.ItemWebSearch        => CodexToolKind.WebSearch,
		Constants.Codex.Protocol.ItemImageView        => CodexToolKind.ImageView,
		_                                             => null,
	};

	public string Summarize(CodexToolKind kind, CodexThreadItem item, bool finished)
	{
		var summary = kind switch
		{
			CodexToolKind.Command    => SummarizeCommand(item, finished),
			CodexToolKind.FileChange => SummarizeFileChange(item),
			CodexToolKind.McpTool    => string.Format(Constants.Codex.Texts.McpToolFormat, item.Server, item.Tool),
			CodexToolKind.WebSearch  => string.Format(Constants.Codex.Texts.WebSearchFormat, item.Query),
			CodexToolKind.ImageView  => string.Format(Constants.Codex.Texts.ImageViewFormat, item.Path),
			_                        => item.Type,
		};
		return OneLine(summary);
	}

	/// <summary>A finished tool item succeeded: status <c>completed</c>, no MCP error, and a command exit code of 0 (or none).</summary>
	public bool Succeeded(CodexToolKind kind, CodexThreadItem item)
	{
		if (!string.Equals(item.Status, Constants.Codex.Protocol.ItemStatusCompleted, StringComparison.Ordinal) && item.Status is not null)
			return false;

		return kind switch
		{
			CodexToolKind.Command => item.ExitCode is null or 0,
			CodexToolKind.McpTool => item.Error is null,
			_                     => true,
		};
	}

	/// <summary>The command a model asked for, without Codex's PowerShell wrapper.</summary>
	public string InnerCommand(string command)
	{
		var match = PowerShellWrapper().Match(command.Trim());
		return match.Success ? match.Groups["inner"].Value.Replace("''", "'") : command;
	}

	private string SummarizeCommand(CodexThreadItem item, bool finished)
	{
		var command = InnerCommand(item.Command ?? string.Empty);
		return finished && item.ExitCode is { } exit && exit != 0
			? string.Format(Constants.Codex.Texts.CommandFailedFormat, command, exit)
			: string.Format(Constants.Codex.Texts.CommandFormat, command);
	}

	private string SummarizeFileChange(CodexThreadItem item)
	{
		var count = item.Changes?.Count ?? 0;
		var first = count > 0 ? item.Changes![0].Path : null;
		return string.IsNullOrEmpty(first)
			? string.Format(Constants.Codex.Texts.FileChangeNoPathFormat, count)
			: string.Format(Constants.Codex.Texts.FileChangeFormat, count, first);
	}

	private string OneLine(string text)
	{
		var line = text.ReplaceLineEndings(" ").Trim();
		return line.Length <= Constants.Codex.ToolSummaryMaxLength
			? line
			: line[..(Constants.Codex.ToolSummaryMaxLength - Constants.Codex.Ellipsis.Length)] + Constants.Codex.Ellipsis;
	}

	[GeneratedRegex(@"^(?:""[^""]*powershell(?:\.exe)?""|\S*powershell(?:\.exe)?)\s+(?:-NoProfile\s+)?-Command\s+'(?<inner>.*)'$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex PowerShellWrapper();
}
