using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ClaudeMaximus.Models;
using Serilog;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public sealed class AttachmentService : IAttachmentService
{
    private static readonly ILogger _log = Log.ForContext<AttachmentService>();

    public async Task<PendingAttachment?> LoadFileAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                _log.Warning("Attachment file not found: {Path}", path);
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(path);
            var name = Path.GetFileName(path);
            return BuildAttachment(name, bytes);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to load attachment from {Path}", path);
            return null;
        }
    }

    public PendingAttachment WrapBytes(string displayName, string mediaType, byte[] data)
    {
        // Trust the caller's media type only if it actually matches the bytes.
        // Otherwise fall through to BuildAttachment which sniffs the magic bytes.
        var actual = SniffImageMagicBytes(data);
        if (actual == null || actual == mediaType)
        {
            return new PendingAttachment
            {
                DisplayName = displayName,
                MediaType = mediaType,
                Data = data,
            };
        }

        _log.Information("Attachment {Name} declared as {Declared} but bytes look like {Actual}; re-detecting",
            displayName, mediaType, actual);
        return BuildAttachment(displayName, data);
    }

    /// <summary>
    /// Builds a PendingAttachment with a media type that's guaranteed to match the bytes.
    /// For images that aren't in Anthropic's supported set (PNG/JPEG/GIF/WebP), the bytes
    /// are re-encoded to PNG via Avalonia's Bitmap (Skia). This prevents historical-image
    /// corruption on session resume — the API only validates content thoroughly when
    /// replaying conversation history.
    /// </summary>
    private PendingAttachment BuildAttachment(string displayName, byte[] bytes)
    {
        // Try magic bytes first — they're authoritative
        var sniffed = SniffImageMagicBytes(bytes);
        if (sniffed != null)
        {
            // Anthropic-supported image: keep as-is, fix the extension if it lies
            if (IsAnthropicSupportedImageType(sniffed))
            {
                return new PendingAttachment
                {
                    DisplayName = NormalizeImageExtension(displayName, sniffed),
                    MediaType = sniffed,
                    Data = bytes,
                };
            }

            // Image but not in the supported set (TIFF/BMP/HEIC/etc.) — re-encode to PNG
            var png = TryConvertToPng(bytes);
            if (png != null)
            {
                _log.Information("Re-encoded attachment {Name} from {Original} to image/png", displayName, sniffed);
                return new PendingAttachment
                {
                    DisplayName = NormalizeImageExtension(displayName, "image/png"),
                    MediaType = "image/png",
                    Data = png,
                };
            }

            _log.Warning("Failed to re-encode {Name} from {Original} to PNG; sending as octet-stream",
                displayName, sniffed);
            return new PendingAttachment
            {
                DisplayName = displayName,
                MediaType = "application/octet-stream",
                Data = bytes,
            };
        }

        // Not a recognised image — fall back to extension for non-image files
        var mediaType = DetectNonImageMediaType(displayName);
        return new PendingAttachment
        {
            DisplayName = displayName,
            MediaType = mediaType,
            Data = bytes,
        };
    }

    /// <summary>
    /// Returns the media type detected from the file's magic bytes, or null if the bytes
    /// don't match any image format we recognise.
    /// </summary>
    private static string? SniffImageMagicBytes(byte[] bytes)
    {
        if (bytes.Length < 4) return null;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";
        // JPEG: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        // GIF: 47 49 46 38
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
            return "image/gif";
        // WebP: RIFF....WEBP
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return "image/webp";
        // TIFF (big-endian): 4D 4D 00 2A
        if (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A)
            return "image/tiff";
        // TIFF (little-endian): 49 49 2A 00
        if (bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00)
            return "image/tiff";
        // BMP: 42 4D
        if (bytes[0] == 0x42 && bytes[1] == 0x4D)
            return "image/bmp";
        // HEIC/HEIF: ftyp box at offset 4, brand "heic"/"heif"/"mif1"/"msf1"
        if (bytes.Length >= 12 && bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70)
        {
            var brand = System.Text.Encoding.ASCII.GetString(bytes, 8, 4);
            if (brand is "heic" or "heix" or "heim" or "heis" or "hevc" or "hevx"
                       or "heif" or "mif1" or "msf1")
                return "image/heic";
        }

        return null;
    }

    private static bool IsAnthropicSupportedImageType(string mediaType) =>
        mediaType is "image/png" or "image/jpeg" or "image/gif" or "image/webp";

    /// <summary>
    /// Replaces the file extension with one matching the actual media type, so the
    /// attachment chip and any persisted display name reflect reality.
    /// </summary>
    private static string NormalizeImageExtension(string fileName, string mediaType)
    {
        var ext = mediaType switch
        {
            "image/png"  => ".png",
            "image/jpeg" => ".jpg",
            "image/gif"  => ".gif",
            "image/webp" => ".webp",
            _ => null,
        };
        if (ext == null) return fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return stem + ext;
    }

    /// <summary>
    /// Decodes any image format Avalonia/Skia understands (TIFF, BMP, HEIC on supported
    /// platforms) and re-encodes it as PNG. Returns null if decoding fails.
    /// </summary>
    private byte[]? TryConvertToPng(byte[] bytes)
    {
        try
        {
            using var sourceStream = new MemoryStream(bytes);
            using var bitmap = new Avalonia.Media.Imaging.Bitmap(sourceStream);
            using var pngStream = new MemoryStream();
            bitmap.Save(pngStream); // Avalonia's Bitmap.Save defaults to PNG
            return pngStream.ToArray();
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Failed to convert image bytes to PNG");
            return null;
        }
    }

    private static string DetectNonImageMediaType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf"  => "application/pdf",
            ".txt" or ".md" or ".log" => "text/plain",
            ".json" => "application/json",
            ".xml"  => "application/xml",
            ".html" or ".htm" => "text/html",
            ".csv"  => "text/csv",
            ".cs" or ".js" or ".ts" or ".py" or ".go" or ".rs" or ".java" or ".rb" or ".sh"
                    => "text/plain",
            _       => "application/octet-stream",
        };
    }

    public List<TessynContentBlock> ToContentBlocks(IEnumerable<PendingAttachment> attachments, string text)
    {
        var blocks = new List<TessynContentBlock>();

        foreach (var att in attachments)
        {
            if (att.IsImage)
            {
                blocks.Add(TessynContentBlock.FromBase64Image(
                    att.MediaType,
                    Convert.ToBase64String(att.Data)));
            }
            else
            {
                // Non-image attachments: inline as a text block describing the file.
                // Daemon stream-json mode currently only supports text + image content blocks.
                // For other file types, we send the file content inline as text (with truncation guard).
                blocks.Add(TessynContentBlock.FromText(BuildFileBlockText(att)));
            }
        }

        if (!string.IsNullOrWhiteSpace(text))
            blocks.Add(TessynContentBlock.FromText(text));

        return blocks;
    }

    private static string BuildFileBlockText(PendingAttachment att)
    {
        // Try to inline as text if the bytes are valid UTF-8 and reasonably sized.
        const int maxInlineBytes = 256 * 1024; // 256 KB hard cap
        if (att.Data.Length <= maxInlineBytes)
        {
            try
            {
                var text = System.Text.Encoding.UTF8.GetString(att.Data);
                // Reject if it contains too many control characters (likely binary)
                var controlCount = 0;
                foreach (var c in text)
                {
                    if (c < 32 && c != '\n' && c != '\r' && c != '\t')
                    {
                        controlCount++;
                        if (controlCount > 16) break;
                    }
                }
                if (controlCount <= 16)
                    return $"<attachment name=\"{att.DisplayName}\" mediaType=\"{att.MediaType}\" size=\"{att.SizeText}\">\n{text}\n</attachment>";
            }
            catch
            {
                // Fall through to placeholder
            }
        }

        return $"[Attachment: {att.DisplayName} ({att.MediaType}, {att.SizeText}) — content not inlined]";
    }

}
