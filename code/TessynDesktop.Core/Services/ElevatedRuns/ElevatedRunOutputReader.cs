using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TessynDesktop.Services;

/// <summary>Reads the end of a run's <c>output.log</c> (FR.15.19, <c>elevated.get</c>): the last lines, each cut, within a character budget. The file may still be written to.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunOutputReader
{
	/// <summary>How much of the end of the file is looked at: far more than 200 lines of 500 characters.</summary>
	private const int MaxTailBytes = 1_048_576;

	/// <summary>
	/// The last <paramref name="maxLines"/> lines of the log, each cut to <paramref name="maxLineLength"/> characters, oldest first, as few of them as fit in
	/// <paramref name="maxChars"/>. Empty when the file is missing or empty.
	/// </summary>
	public IReadOnlyList<string> ReadTail(string logPath, int maxLines, int maxLineLength, int maxChars)
	{
		if (maxLines <= 0 || !File.Exists(logPath))
			return [];

		string text;
		bool fromStart;
		try
		{
			using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			var start = Math.Max(0, stream.Length - MaxTailBytes);
			fromStart = start == 0;
			stream.Seek(start, SeekOrigin.Begin);
			using var reader = new StreamReader(stream, Encoding.UTF8);
			text = reader.ReadToEnd();
		}
		catch (IOException)
		{
			return [];
		}

		var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
		if (!fromStart && lines.Count > 0)
			lines.RemoveAt(0); // began in the middle of a line

		while (lines.Count > 0 && lines[^1].Length == 0)
			lines.RemoveAt(lines.Count - 1);

		var picked = new List<string>();
		var total  = 0;
		for (var i = lines.Count - 1; i >= 0 && picked.Count < maxLines; i--)
		{
			var line = lines[i].Length > maxLineLength ? lines[i][..maxLineLength] : lines[i];
			if (total + line.Length + 1 > maxChars)
				break;

			total += line.Length + 1;
			picked.Add(line);
		}

		picked.Reverse();
		return picked;
	}
}
