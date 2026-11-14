using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TessynDesktop.Services;

/// <summary>
/// Finds the header lines of a session file by scanning its bytes. A header is decided by the session-file parser's own
/// rule (<see cref="SessionFileService.TryParseHeader"/>), only lines that start with "[" are decoded, and lines end at
/// LF, CR or CRLF exactly as the parser's <c>StreamReader</c> reads them.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class SessionFileScanner
{
	private const byte LineFeed = 0x0A;
	private const byte CarriageReturn = 0x0D;
	private const byte OpenBracket = 0x5B;

	/// <summary>Offset of the first line: past the UTF-8 byte order mark when the file starts with one.</summary>
	public long FirstLineOffset(FileStream stream)
	{
		if (stream.Length < Constants.ContentStore.Utf8BomLength)
			return 0;

		var start = new byte[Constants.ContentStore.Utf8BomLength];
		stream.Seek(0, SeekOrigin.Begin);
		stream.ReadExactly(start);
		var preamble = Encoding.UTF8.GetPreamble();
		return start.AsSpan().SequenceEqual(preamble) ? preamble.Length : 0;
	}

	/// <summary>Headers whose line starts in [<paramref name="from"/>, <paramref name="to"/>); <paramref name="from"/> must be a line start.</summary>
	public List<ScannedHeader> Scan(FileStream stream, long from, long to)
	{
		var headers = new List<ScannedHeader>();
		var buffer  = new byte[Constants.ContentStore.ScanBufferBytes];
		var prefix  = new byte[Constants.ContentStore.HeaderPrefixMaxBytes];

		var prefixLength = 0;
		var candidate    = false;
		var carriage     = false;
		var lineStart    = from;
		var position     = from;

		stream.Seek(from, SeekOrigin.Begin);
		while (position < to)
		{
			var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, to - position));
			if (read <= 0)
				break;

			for (var i = 0; i < read; i++)
			{
				var b        = buffer[i];
				var absolute = position + i;

				if (carriage)
				{
					carriage = false;
					if (b == LineFeed)
					{
						lineStart = absolute + 1;
						continue;
					}
				}

				if (b == LineFeed || b == CarriageReturn)
				{
					EndLine(headers, candidate, prefix, prefixLength, lineStart);
					candidate    = false;
					prefixLength = 0;
					lineStart    = absolute + 1;
					carriage     = b == CarriageReturn;
					continue;
				}

				if (absolute == lineStart && b == OpenBracket)
					candidate = true;

				if (candidate && prefixLength < prefix.Length)
					prefix[prefixLength++] = b;
			}

			position += read;
		}

		EndLine(headers, candidate, prefix, prefixLength, lineStart);
		return headers;
	}

	private static void EndLine(List<ScannedHeader> headers, bool candidate, byte[] prefix, int prefixLength, long lineStart)
	{
		if (!candidate)
			return;

		var line = Encoding.UTF8.GetString(prefix, 0, prefixLength);
		if (!SessionFileService.TryParseHeader(line, out var timestamp, out var role, out var rawMeta))
			return;

		string? profile = null;
		string? model   = null;
		string? effort  = null;
		if (role != Constants.SessionFile.RoleCompaction)
		{
			var meta = SessionFileService.ParseMetadata(rawMeta);
			meta.TryGetValue("profile", out profile);
			meta.TryGetValue("model", out model);
			meta.TryGetValue("effort", out effort);
		}

		headers.Add(new ScannedHeader(lineStart, timestamp, role, profile, model, effort));
	}
}
