using System;

namespace ClaudeMaximus.Models;

/// <summary>
/// An attachment staged in the input area, ready to be sent with the next message.
/// Created from a file picker, drag-and-drop, or clipboard paste.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PendingAttachment
{
    /// <summary>Unique ID for tracking and removal from the chips UI.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Original filename for display (e.g. "screenshot.png", "report.pdf").</summary>
    public required string DisplayName { get; init; }

    /// <summary>MIME type, e.g. "image/png", "application/pdf", "text/plain".</summary>
    public required string MediaType { get; init; }

    /// <summary>Raw bytes of the file/image.</summary>
    public required byte[] Data { get; init; }

    /// <summary>True for any image MIME type — used to render a thumbnail in the chip.</summary>
    public bool IsImage => MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Human-readable size, e.g. "245 KB", "1.2 MB".</summary>
    public string SizeText => FormatBytes(Data.Length);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F0} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
