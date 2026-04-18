using ReactiveUI;

namespace ClaudeMaximus.ViewModels;

/// <summary>
/// Base class for content blocks within an assistant message.
/// Each block type gets its own DataTemplate in SessionView.axaml.
/// </summary>
/// <remarks>Created by Claude</remarks>
public abstract class MessageBlockViewModel : ViewModelBase
{
    private bool _isStreaming;
    private bool _isExpanded;

    /// <summary>Block index from the daemon event, used to route deltas to the correct block.</summary>
    public int BlockIndex { get; init; }

    /// <summary>True while the block is actively receiving data.</summary>
    public bool IsStreaming
    {
        get => _isStreaming;
        set => this.RaiseAndSetIfChanged(ref _isStreaming, value);
    }

    /// <summary>True when the block's details are expanded (for collapsible blocks).</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }
}

/// <summary>
/// A text content block — Claude's main response text.
/// Rendered via MarkdownView or HighlightTextBlock.
/// </summary>
public sealed class TextBlockViewModel : MessageBlockViewModel
{
    private string _text = string.Empty;

    public string Text
    {
        get => _text;
        set => this.RaiseAndSetIfChanged(ref _text, value);
    }

    public void AppendText(string delta) => Text += delta;
}

/// <summary>
/// An extended thinking/reasoning block — Claude's internal reasoning.
/// Rendered dimmed and collapsible.
/// </summary>
public sealed class ThinkingBlockViewModel : MessageBlockViewModel
{
    private string _text = string.Empty;

    public string Text
    {
        get => _text;
        set => this.RaiseAndSetIfChanged(ref _text, value);
    }

    public void AppendText(string delta) => Text += delta;
}

/// <summary>
/// A tool use block — a tool call Claude is making (Bash, Read, Edit, etc.).
/// Shows tool name + input summary, with status icon (running/success/error).
/// Tool result is stored here too (not as a separate peer block).
/// </summary>
public sealed class ToolUseBlockViewModel : MessageBlockViewModel
{
    private string _statusIcon = "⟳";
    private string _resultSummary = string.Empty;
    private string _resultContent = string.Empty;
    private bool _isError;

    public string ToolName { get; init; } = "unknown";

    private string _inputSummary = string.Empty;

    /// <summary>Input summary (e.g. file path for Read, command for Bash).</summary>
    public string InputSummary
    {
        get => _inputSummary;
        set => this.RaiseAndSetIfChanged(ref _inputSummary, value);
    }

    /// <summary>Full input as JSON string, shown when expanded.</summary>
    public string? FullInput { get; set; }

    /// <summary>Status icon: ⟳ running, ✓ success, ✗ error.</summary>
    public string StatusIcon
    {
        get => _statusIcon;
        set => this.RaiseAndSetIfChanged(ref _statusIcon, value);
    }

    /// <summary>Short description of the result (e.g. "3 lines", "247 lines").</summary>
    public string ResultSummary
    {
        get => _resultSummary;
        set => this.RaiseAndSetIfChanged(ref _resultSummary, value);
    }

    /// <summary>Full result content (truncated for display).</summary>
    public string ResultContent
    {
        get => _resultContent;
        set => this.RaiseAndSetIfChanged(ref _resultContent, value);
    }

    public bool IsError
    {
        get => _isError;
        set => this.RaiseAndSetIfChanged(ref _isError, value);
    }

    /// <summary>Mark this tool call as completed successfully.</summary>
    public void Complete(string? resultContent = null)
    {
        IsStreaming = false;
        StatusIcon = "✓";
        if (resultContent != null)
        {
            var lines = resultContent.Split('\n');
            ResultSummary = lines.Length > 1 ? $"({lines.Length} lines)" : "";
            // Truncate for display — first 15 lines
            ResultContent = lines.Length > 15
                ? string.Join('\n', lines[..15]) + $"\n… ({lines.Length - 15} more lines)"
                : resultContent;
        }
    }

    /// <summary>Mark this tool call as failed.</summary>
    public void Fail(string? errorContent = null)
    {
        IsStreaming = false;
        IsError = true;
        StatusIcon = "✗";
        if (errorContent != null)
            ResultContent = errorContent;
    }

    /// <summary>Extract a readable input summary from the tool name and raw input.</summary>
    public static string SummarizeInput(string toolName, string? rawInput)
    {
        if (string.IsNullOrEmpty(rawInput)) return "";

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawInput);
            var root = doc.RootElement;

            return toolName switch
            {
                "Bash" => root.TryGetProperty("command", out var cmd)
                    ? TruncateLine(cmd.GetString() ?? "", 80) : "",
                "Read" => root.TryGetProperty("file_path", out var fp)
                    ? fp.GetString() ?? "" : "",
                "Edit" => root.TryGetProperty("file_path", out var ef)
                    ? ef.GetString() ?? "" : "",
                "Write" => root.TryGetProperty("file_path", out var wf)
                    ? wf.GetString() ?? "" : "",
                "Glob" => root.TryGetProperty("pattern", out var gp)
                    ? gp.GetString() ?? "" : "",
                "Grep" => root.TryGetProperty("pattern", out var grp)
                    ? grp.GetString() ?? "" : "",
                "WebSearch" => root.TryGetProperty("query", out var q)
                    ? q.GetString() ?? "" : "",
                "WebFetch" => root.TryGetProperty("url", out var u)
                    ? TruncateLine(u.GetString() ?? "", 60) : "",
                _ => "",
            };
        }
        catch
        {
            return "";
        }
    }

    private static string TruncateLine(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
