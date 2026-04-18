using System.Collections.Generic;
using System.Threading.Tasks;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <summary>
/// Loads files into PendingAttachment objects ready to be sent as content blocks.
/// Handles MIME type detection.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IAttachmentService
{
    /// <summary>Read a file from disk into a PendingAttachment. Returns null on error.</summary>
    Task<PendingAttachment?> LoadFileAsync(string path);

    /// <summary>Wrap raw bytes (e.g. from a clipboard image) into a PendingAttachment.</summary>
    PendingAttachment WrapBytes(string displayName, string mediaType, byte[] data);

    /// <summary>Convert a list of attachments to TessynContentBlock[] for run.send.</summary>
    List<TessynContentBlock> ToContentBlocks(IEnumerable<PendingAttachment> attachments, string text);
}
